using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace WhatsappClient.Controllers
{
    // =========================
    // DTOs (WebApp -> WebApp)
    // =========================
    public class UpdateConversationStatusDto
    {
        public int ConversationId { get; set; }
        public string Status { get; set; } = "open"; // solo permitimos cerrar desde la web
        public string? Reason { get; set; }
    }

    public class ConversationIdDto
    {
        public int ConversationId { get; set; }
    }

    public class AssignConversationDto
    {
        public int ConversationId { get; set; }
        public int? ToUserId { get; set; } // null => soltar
        public string? Reason { get; set; } // hoy el API no la persiste (tu API la ignora)
    }

    public class HoldConversationDto
    {
        public int ConversationId { get; set; }
        public string? Reason { get; set; }
    }

    public class SendMessageDto
    {
        public int ConversationId { get; set; }
        public int? ContactId { get; set; }
        public string? ContactPhone { get; set; }
        public string? Message { get; set; }
    }

    [Authorize]
    [Route("Chat/[action]")]
    public class ChatController : Controller
    {
        private readonly IConfiguration _cfg;
        private readonly IHttpClientFactory _httpFactory;

        // Si en algún momento reactivás autocierre, aquí queda el backing store.
        private static readonly ConcurrentDictionary<int, CancellationTokenSource> _autoClose = new();
        private static readonly TimeSpan AUTO_CLOSE_AFTER = TimeSpan.FromHours(23);

        public ChatController(IConfiguration cfg, IHttpClientFactory httpFactory)
        {
            _cfg = cfg;
            _httpFactory = httpFactory;
        }

        [HttpGet]
        [Route("", Name = "ChatRoot")]
        public IActionResult Index() => View();

        // =========================
        // ME (claims del usuario autenticado)
        // =========================
        [HttpGet]
        public IActionResult Me()
        {
            var me = GetMe();
            return Ok(new { userId = me.userId, profileId = me.profileId, isAdmin = me.isAdmin });
        }

        // =========================
        // AGENTES (perfil 1)
        // =========================
        [HttpGet]
        public async Task<IActionResult> GetAgents()
        {
            try
            {
                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { agents = Array.Empty<object>(), error = reason });

                var resp = await http.GetAsync("api/seguridad/user/by-perfil-id/1");
                var json = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    return Ok(new { agents = Array.Empty<object>(), error = "No se pudo obtener agentes" });

                using var doc = JsonDocument.Parse(json);

                var agents = ExtraerItems(doc.RootElement)
                    .Select(it => new
                    {
                        id = GetIntFlex(it, "id", "Id") ?? 0,
                        name = GetStringFlex(it, "name", "Name", "fullName", "FullName", "username", "Username", "nombre", "Nombre") ?? ""
                    })
                    .Where(a => a.id > 0)
                    .ToList();

                return Ok(new { agents });
            }
            catch (Exception ex)
            {
                return Ok(new { agents = Array.Empty<object>(), error = ex.Message });
            }
        }

        // =========================
        // PANEL IZQ (Cards)
        // - Usa api/general/conversation/panel si existe
        // - Incluye semáforo (green/red/orange) + flags (isMine, canWrite)
        // =========================
        [HttpGet]
        public async Task<IActionResult> GetAllConversations()
        {
            try
            {
                var me = GetMe();

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { conversations = Array.Empty<object>(), error = reason });

                string convRaw;
                var resPanel = await http.GetAsync("api/general/conversation/panel");
                convRaw = await resPanel.Content.ReadAsStringAsync();
                var usedPanel = resPanel.IsSuccessStatusCode;

                if (!usedPanel)
                {
                    // fallback
                    var resConv = await http.GetAsync("api/general/conversation");
                    convRaw = await resConv.Content.ReadAsStringAsync();
                    if (!resConv.IsSuccessStatusCode)
                        return Ok(new { conversations = Array.Empty<object>(), error = "No se pudo obtener conversaciones" });
                }

                // contactos -> teléfono y nombre
                var phoneByContact = new Dictionary<int, string?>();
                var nameByContact = new Dictionary<int, string?>();

                var resContact = await http.GetAsync("api/general/contact");
                if (resContact.IsSuccessStatusCode)
                {
                    using var docC = JsonDocument.Parse(await resContact.Content.ReadAsStringAsync());
                    foreach (var c in ExtraerItems(docC.RootElement))
                    {
                        var id = GetIntFlex(c, "id", "Id") ?? 0;
                        if (id <= 0) continue;

                        var phone = GetStringFlex(c, "phone_number", "phoneNumber", "PhoneNumber", "phone", "Phone", "telefono", "Telefono");
                        var name = GetStringFlex(c, "name", "Name", "nombre", "Nombre", "fullName", "FullName");

                        phoneByContact[id] = phone;
                        nameByContact[id] = name;
                    }
                }

                // agentes -> assignedUserName
                var agentNameById = new Dictionary<int, string>();
                var resAgents = await http.GetAsync("api/seguridad/user/by-perfil-id/1");
                if (resAgents.IsSuccessStatusCode)
                {
                    using var adoc = JsonDocument.Parse(await resAgents.Content.ReadAsStringAsync());
                    foreach (var a in ExtraerItems(adoc.RootElement))
                    {
                        var id = GetIntFlex(a, "id", "Id") ?? 0;
                        if (id <= 0) continue;

                        var nm = GetStringFlex(a, "name", "Name", "fullName", "FullName", "username", "Username", "nombre", "Nombre") ?? $"User {id}";
                        agentNameById[id] = nm;
                    }
                }

                using var docConv = JsonDocument.Parse(convRaw);

                var convsRaw = ExtraerItems(docConv.RootElement)
                    .Select(e => new
                    {
                        id = GetIntFlex(e, "id", "Id") ?? 0,
                        contactId = GetIntFlex(e, "contact_id", "ContactId", "contactId") ?? 0,
                        status = GetStringFlex(e, "status", "Status") ?? "open",
                        startedAt = GetDateFlex(e, "started_at", "StartedAt", "startedAt"),
                        lastActivityAt = GetDateFlex(e, "last_activity_at", "LastActivityAt", "lastActivityAt"),
                        agentRequestedAt = GetDateFlex(e, "agent_requested_at", "AgentRequestedAt", "agentRequestedAt"),
                        assignedUserId = GetIntFlex(e, "assigned_user_id", "AssignedUserId", "assignedUserId"),
                        isOnHold = GetBoolFlex(e, "is_on_hold", "IsOnHold") ?? false,
                        onHoldReason = GetStringFlex(e, "on_hold_reason", "OnHoldReason") // puede venir null
                    })
                    .ToList();

                // si no fue panel, mantenemos filtro viejo: solo las que pidieron agente
                if (!usedPanel)
                    convsRaw = convsRaw.Where(x => x.agentRequestedAt != null).ToList();

                var convs = convsRaw
                    .Select(x =>
                    {
                        var phone = phoneByContact.TryGetValue(x.contactId, out var ph) ? ph : null;
                        var cname = nameByContact.TryGetValue(x.contactId, out var cn) ? cn : null;

                        string? assignedName = null;
                        if (x.assignedUserId.HasValue && x.assignedUserId.Value > 0 && agentNameById.TryGetValue(x.assignedUserId.Value, out var an))
                            assignedName = an;

                        var isMine = x.assignedUserId.HasValue && x.assignedUserId.Value == me.userId;
                        var canWrite = me.isAdmin || (isMine && !x.isOnHold && IsOpen(x.status));

                        // Semáforo:
                        // - orange: en espera
                        // - red: asignada a alguien
                        // - green: libre
                        var traffic =
                            x.isOnHold ? "orange" :
                            (x.assignedUserId.HasValue && x.assignedUserId.Value > 0) ? "red" :
                            "green";

                        // Bloqueo visual: asignada a otro (solo para agentes)
                        var lockedByOther = !me.isAdmin
                            && x.assignedUserId.HasValue
                            && x.assignedUserId.Value > 0
                            && x.assignedUserId.Value != me.userId;

                        return new
                        {
                            id = x.id,
                            contactId = x.contactId,
                            contactPhone = phone,
                            contactName = cname,

                            status = x.status,
                            startedAt = x.startedAt,
                            lastActivityAt = x.lastActivityAt,
                            agentRequestedAt = x.agentRequestedAt,

                            assignedUserId = x.assignedUserId,
                            assignedUserName = assignedName,

                            isOnHold = x.isOnHold,
                            onHoldReason = x.onHoldReason,

                            // helpers para el front
                            traffic,
                            isMine,
                            canWrite,
                            lockedByOther
                        };
                    })
                    .OrderByDescending(x => x.lastActivityAt ?? x.startedAt)
                    .ToList();

                return Ok(new { conversations = convs });
            }
            catch (Exception ex)
            {
                return Ok(new { conversations = Array.Empty<object>(), error = ex.Message });
            }
        }

        // =========================
        // MENSAJES por conversación (Read-only permitido a todos)
        // =========================
        [HttpGet]
        public async Task<IActionResult> GetConversationMessages(int conversationId)
        {
            try
            {
                if (conversationId <= 0)
                    return Ok(new { messages = Array.Empty<object>(), error = "conversationId inválido" });

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { messages = Array.Empty<object>(), error = reason });

                var resMsg = await http.GetAsync("api/general/message");
                var msgRaw = await resMsg.Content.ReadAsStringAsync();

                if (!resMsg.IsSuccessStatusCode)
                    return Ok(new { messages = Array.Empty<object>(), error = "No se pudo obtener mensajes" });

                using var doc = JsonDocument.Parse(msgRaw);
                var msgItems = ExtraerItems(doc.RootElement).ToList();

                var attachments = new List<AttachmentInfo>();
                try
                {
                    var resAtt = await http.GetAsync("api/general/attachment");
                    if (resAtt.IsSuccessStatusCode)
                    {
                        using var docA = JsonDocument.Parse(await resAtt.Content.ReadAsStringAsync());
                        attachments = ExtraerItems(docA.RootElement)
                            .Select(e => new AttachmentInfo
                            {
                                Id = GetIntFlex(e, "id", "Id") ?? 0,
                                MessageId = GetIntFlex(e, "message_id", "MessageId") ?? 0,
                                FileName = GetStringFlex(e, "file_name", "FileName") ?? string.Empty,
                                MimeType = GetStringFlex(e, "mime_type", "MimeType") ?? "application/octet-stream",
                                SizeBytes = GetLongFlex(e, "size_bytes", "SizeBytes")
                            })
                            .Where(a => a.Id > 0 && a.MessageId > 0)
                            .ToList();
                    }
                }
                catch { /* ignore */ }

                var attachmentsByMessage = attachments
                    .GroupBy(a => a.MessageId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var msgs = msgItems
                    .Where(e => (GetIntFlex(e, "conversation_id", "ConversationId", "conversationId") ?? 0) == conversationId)
                    .Select(e =>
                    {
                        var id = GetIntFlex(e, "id", "Id") ?? 0;
                        attachmentsByMessage.TryGetValue(id, out var attsForMsg);
                        attsForMsg ??= new List<AttachmentInfo>();

                        return new
                        {
                            id,
                            sender = GetStringFlex(e, "sender", "Sender") ?? "contact",
                            message = GetStringFlex(e, "message", "Message", "messages", "Messages") ?? string.Empty,
                            type = GetStringFlex(e, "type", "Type") ?? "text",
                            sentAt = GetDateFlex(e, "sent_at", "SentAt", "sentAt"),
                            attachments = attsForMsg.Select(a => new
                            {
                                id = a.Id,
                                fileName = a.FileName,
                                mimeType = a.MimeType,
                                sizeBytes = a.SizeBytes
                            }).ToList()
                        };
                    })
                    .OrderBy(x => x.sentAt ?? DateTime.MinValue)
                    .ToList();

                return Ok(new { messages = msgs });
            }
            catch (Exception ex)
            {
                return Ok(new { messages = Array.Empty<object>(), error = ex.Message });
            }
        }

        // =========================
        // SEND TEXT (solo si puedo escribir)
        // Reglas:
        // - closed => no
        // - onHold => no (except admin)
        // - assigned a otro => no (si no admin)
        // - libre => no escribe hasta "tomar" (except admin)
        // =========================
        [HttpPost]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageDto req)
        {
            try
            {
                if (req == null || req.ConversationId <= 0)
                    return Ok(new { success = false, error = "conversationId requerido" });

                var text = (req.Message ?? "").Trim();
                if (string.IsNullOrWhiteSpace(text))
                    return Ok(new { success = false, error = "message requerido" });

                var me = GetMe();

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { success = false, error = reason });

                var meta = await GetConversationMetaAsync(http, req.ConversationId);
                if (meta == null)
                    return Ok(new { success = false, error = "Conversación no encontrada" });

                var canWrite = CanWrite(meta, me);
                if (!canWrite)
                    return Ok(new { success = false, error = BuildWriteBlockReason(meta, me) });

                // resolver teléfono
                var contactId = (req.ContactId.HasValue && req.ContactId.Value > 0) ? req.ContactId.Value : meta.ContactId;
                var contactPhone = string.IsNullOrWhiteSpace(req.ContactPhone) ? null : req.ContactPhone;

                if (string.IsNullOrWhiteSpace(contactPhone))
                {
                    contactPhone = await ResolveContactPhoneAsync(http, contactId);
                }

                if (string.IsNullOrWhiteSpace(contactPhone))
                    return Ok(new { success = false, error = "No se pudo resolver el teléfono del contacto" });

                var apiRes = await SendTextViaApiAsync(http, new
                {
                    Contact_Id = contactId > 0 ? (int?)contactId : null,
                    Conversation_Id = meta.Id,
                    To_Phone = contactPhone,
                    Text = text,
                    Create_If_Not_Exists = false,
                    Log = true
                });

                if (!apiRes.success)
                    return Ok(new { success = false, error = apiRes.error });

                return Ok(new { success = true, conversationId = apiRes.conversationId ?? meta.Id, justCreated = apiRes.justCreated });
            }
            catch (Exception ex)
            {
                return Ok(new { success = false, error = ex.Message });
            }
        }

        // =========================
        // SEND AUDIO (solo si puedo escribir)
        // Nota: removí la route "~/api/..." para no colisionar ni exponer rutas tipo API desde la WebApp.
        // =========================
        [HttpPost]
        [Consumes("multipart/form-data")]
        [Route("~/Chat/SendAudio")]
        public async Task<IActionResult> SendAudio(
            [FromForm] IFormFile file,
            [FromForm] int conversationId,
            [FromForm] int? contactId,
            [FromForm] string? contactPhone)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return Ok(new { success = false, error = "No se recibió archivo de audio." });

                if (conversationId <= 0)
                    return Ok(new { success = false, error = "conversationId inválido" });

                var me = GetMe();

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { success = false, error = reason });

                var meta = await GetConversationMetaAsync(http, conversationId);
                if (meta == null)
                    return Ok(new { success = false, error = "Conversación no encontrada" });

                var canWrite = CanWrite(meta, me);
                if (!canWrite)
                    return Ok(new { success = false, error = BuildWriteBlockReason(meta, me) });

                var finalContactId = (contactId.HasValue && contactId.Value > 0) ? contactId.Value : meta.ContactId;
                if (finalContactId <= 0)
                    return Ok(new { success = false, error = "No se pudo resolver contactId para enviar el audio." });

                if (string.IsNullOrWhiteSpace(contactPhone))
                    contactPhone = await ResolveContactPhoneAsync(http, finalContactId);

                if (string.IsNullOrWhiteSpace(contactPhone))
                    return Ok(new { success = false, error = "No se pudo resolver el teléfono del contacto." });

                var toPhoneDigits = SoloDigitos(contactPhone);
                if (string.IsNullOrWhiteSpace(toPhoneDigits))
                    return Ok(new { success = false, error = "No se pudo normalizar el teléfono del contacto." });

                byte[] bytes;
                await using (var ms = new MemoryStream())
                {
                    await file.CopyToAsync(ms);
                    bytes = ms.ToArray();
                }

                var mime = (file.ContentType ?? "audio/webm").Trim();
                if (mime.Contains(";")) mime = mime.Split(';')[0].Trim();

                var forwardedName = NormalizeAudioFileName(file.FileName, mime);

                // 1) Guardar en API (si tu backend lo requiere)
                using (var saveContent = new MultipartFormDataContent())
                {
                    var sc = new StreamContent(new MemoryStream(bytes));
                    sc.Headers.ContentType = new MediaTypeHeaderValue(mime);

                    saveContent.Add(sc, "file", forwardedName);
                    saveContent.Add(new StringContent(conversationId.ToString()), "conversationId");
                    saveContent.Add(new StringContent(finalContactId.ToString()), "contactId");
                    saveContent.Add(new StringContent(toPhoneDigits), "toPhone");

                    var saveResp = await http.PostAsync("api/integraciones/whatsapp/agent/audio", saveContent);
                    var saveBody = await saveResp.Content.ReadAsStringAsync();

                    if (!saveResp.IsSuccessStatusCode)
                    {
                        var msg = ExtractApiError(saveBody) ?? $"API {(int)saveResp.StatusCode}: {saveBody}";
                        if (saveResp.StatusCode == HttpStatusCode.Unauthorized)
                            msg = "No autorizado (token expirado). Vuelva a iniciar sesión.";
                        return Ok(new { success = false, error = msg });
                    }
                }

                // 2) Enviar por whatsapp (backend)
                using (var sendContent = new MultipartFormDataContent())
                {
                    var sc2 = new StreamContent(new MemoryStream(bytes));
                    sc2.Headers.ContentType = new MediaTypeHeaderValue(mime);

                    sendContent.Add(sc2, "file", forwardedName);
                    sendContent.Add(new StringContent(conversationId.ToString()), "Conversation_Id");
                    sendContent.Add(new StringContent(conversationId.ToString()), "ConversationId");
                    sendContent.Add(new StringContent(finalContactId.ToString()), "Contact_Id");
                    sendContent.Add(new StringContent(finalContactId.ToString()), "ContactId");
                    sendContent.Add(new StringContent(toPhoneDigits), "To_Phone");
                    sendContent.Add(new StringContent("false"), "Create_If_Not_Exists");
                    sendContent.Add(new StringContent("true"), "Log");

                    var sendResp = await http.PostAsync("api/integraciones/whatsapp/send/audio", sendContent);
                    var sendBody = await sendResp.Content.ReadAsStringAsync();

                    if (sendResp.StatusCode == HttpStatusCode.Unauthorized)
                        return Ok(new { success = false, error = "No autorizado (token expirado). Vuelva a iniciar sesión." });

                    var okSend = sendResp.IsSuccessStatusCode && ExtractApiOk(sendBody);
                    if (!okSend)
                    {
                        var msg = ExtractApiError(sendBody) ?? $"API {(int)sendResp.StatusCode}: {sendBody}";
                        return Ok(new { success = false, error = msg });
                    }
                }

                return Ok(new { success = true, conversationId });
            }
            catch (Exception ex)
            {
                return Ok(new { success = false, error = ex.Message });
            }
        }

        // =========================
        // HOLD / RESUME / TAKE / TRANSFER / RELEASE
        // (toda la lógica de bloqueo se aplica acá en la WebApp)
        // =========================

        // Tomar conversación (verde -> roja asignada a mí)
        [HttpPost]
        public async Task<IActionResult> TakeConversation([FromBody] ConversationIdDto req)
        {
            try
            {
                if (req == null || req.ConversationId <= 0)
                    return Ok(new { ok = false, error = "conversationId inválido" });

                var me = GetMe();

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { ok = false, error = reason });

                var meta = await GetConversationMetaAsync(http, req.ConversationId);
                if (meta == null) return Ok(new { ok = false, error = "Conversación no encontrada" });

                if (!me.isAdmin)
                {
                    // si ya tiene dueño y no soy yo => no
                    if (meta.AssignedUserId.HasValue && meta.AssignedUserId.Value > 0 && meta.AssignedUserId.Value != me.userId)
                        return Ok(new { ok = false, error = "La conversación ya está asignada a otro agente." });

                    // si está libre, solo puedo asignármela a mí
                    // (esto es equivalente a tu regla del API)
                }

                var payload = JsonSerializer.Serialize(new { toUserId = me.userId });
                var resp = await http.PostAsync($"api/general/conversation/{req.ConversationId}/assign",
                    new StringContent(payload, Encoding.UTF8, "application/json"));

                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    return Ok(new { ok = false, error = body });

                return Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return Ok(new { ok = false, error = ex.Message });
            }
        }

        // Transferir (roja mía -> roja de otro) o Admin -> cualquiera
        [HttpPost]
        public async Task<IActionResult> TransferConversation([FromBody] AssignConversationDto req)
        {
            try
            {
                if (req == null || req.ConversationId <= 0 || !req.ToUserId.HasValue || req.ToUserId.Value <= 0)
                    return Ok(new { ok = false, error = "payload inválido" });

                var me = GetMe();

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { ok = false, error = reason });

                var meta = await GetConversationMetaAsync(http, req.ConversationId);
                if (meta == null) return Ok(new { ok = false, error = "Conversación no encontrada" });

                if (!me.isAdmin)
                {
                    // solo el dueño puede transferir
                    if (!meta.AssignedUserId.HasValue || meta.AssignedUserId.Value != me.userId)
                        return Ok(new { ok = false, error = "Debes tener tomada la conversación para transferirla." });
                }

                var payload = JsonSerializer.Serialize(new { toUserId = req.ToUserId.Value });
                var resp = await http.PostAsync($"api/general/conversation/{req.ConversationId}/assign",
                    new StringContent(payload, Encoding.UTF8, "application/json"));

                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    return Ok(new { ok = false, error = body });

                return Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return Ok(new { ok = false, error = ex.Message });
            }
        }

        // Soltar (roja mía -> verde). Nota: tu API NO tiene /release; se hace con /assign null.
        [HttpPost]
        public async Task<IActionResult> ReleaseConversation([FromBody] ConversationIdDto req)
        {
            try
            {
                if (req == null || req.ConversationId <= 0)
                    return Ok(new { ok = false, error = "conversationId inválido" });

                var me = GetMe();

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { ok = false, error = reason });

                var meta = await GetConversationMetaAsync(http, req.ConversationId);
                if (meta == null) return Ok(new { ok = false, error = "Conversación no encontrada" });

                if (!me.isAdmin)
                {
                    if (!meta.AssignedUserId.HasValue || meta.AssignedUserId.Value != me.userId)
                        return Ok(new { ok = false, error = "Solo el agente asignado puede soltar esta conversación." });
                }

                var payload = JsonSerializer.Serialize(new { toUserId = (int?)null });
                var resp = await http.PostAsync($"api/general/conversation/{req.ConversationId}/assign",
                    new StringContent(payload, Encoding.UTF8, "application/json"));

                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    return Ok(new { ok = false, error = body });

                return Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return Ok(new { ok = false, error = ex.Message });
            }
        }

        // Poner en espera (naranja)
        [HttpPost]
        public async Task<IActionResult> HoldConversation([FromBody] HoldConversationDto req)
        {
            try
            {
                if (req == null || req.ConversationId <= 0)
                    return Ok(new { ok = false, error = "payload inválido" });

                var me = GetMe();

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { ok = false, error = reason });

                var meta = await GetConversationMetaAsync(http, req.ConversationId);
                if (meta == null) return Ok(new { ok = false, error = "Conversación no encontrada" });

                if (!me.isAdmin)
                {
                    // regla: solo dueño la puede poner en espera
                    if (!meta.AssignedUserId.HasValue || meta.AssignedUserId.Value != me.userId)
                        return Ok(new { ok = false, error = "Debes tener tomada la conversación para ponerla en espera." });
                }

                if (!IsOpen(meta.Status))
                    return Ok(new { ok = false, error = "No se permite poner en espera una conversación cerrada." });

                var reasonText = (req.Reason ?? "").Trim();
                if (string.IsNullOrWhiteSpace(reasonText))
                    reasonText = "Pendiente de seguimiento / información del cliente";

                var payload = JsonSerializer.Serialize(new { reason = reasonText });
                var resp = await http.PostAsync($"api/general/conversation/{req.ConversationId}/hold",
                    new StringContent(payload, Encoding.UTF8, "application/json"));

                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    return Ok(new { ok = false, error = body });

                return Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return Ok(new { ok = false, error = ex.Message });
            }
        }

        // Quitar espera (naranja -> roja/verde según assigned)
        [HttpPost]
        public async Task<IActionResult> ResumeConversation([FromBody] ConversationIdDto req)
        {
            try
            {
                if (req == null || req.ConversationId <= 0)
                    return Ok(new { ok = false, error = "payload inválido" });

                var me = GetMe();

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { ok = false, error = reason });

                var meta = await GetConversationMetaAsync(http, req.ConversationId);
                if (meta == null) return Ok(new { ok = false, error = "Conversación no encontrada" });

                if (!me.isAdmin)
                {
                    // regla: solo dueño puede reanudar (si está asignada a él)
                    if (!meta.AssignedUserId.HasValue || meta.AssignedUserId.Value != me.userId)
                        return Ok(new { ok = false, error = "Solo el agente asignado puede reanudar esta conversación." });
                }

                if (!IsOpen(meta.Status))
                    return Ok(new { ok = false, error = "No se permite reanudar una conversación cerrada." });

                var resp = await http.PostAsync($"api/general/conversation/{req.ConversationId}/resume",
                    new StringContent("{}", Encoding.UTF8, "application/json"));

                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    return Ok(new { ok = false, error = body });

                return Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return Ok(new { ok = false, error = ex.Message });
            }
        }

        // =========================
        // CERRAR (usa el endpoint dedicado /close del API)
        // =========================
        [HttpPost]
        public async Task<IActionResult> CloseConversation([FromBody] UpdateConversationStatusDto req)
        {
            try
            {
                if (req == null || req.ConversationId <= 0)
                    return Ok(new { success = false, error = "payload inválido" });

                // solo permitimos cerrar desde acá
                if (req.Status.Equals("open", StringComparison.OrdinalIgnoreCase))
                    return Ok(new { success = false, error = "No se permite reabrir conversaciones cerradas." });

                var me = GetMe();

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { success = false, error = reason });

                var meta = await GetConversationMetaAsync(http, req.ConversationId);
                if (meta == null) return Ok(new { success = false, error = "Conversación no encontrada" });

                // Solo dueño o admin cierra
                if (!me.isAdmin)
                {
                    if (!meta.AssignedUserId.HasValue || meta.AssignedUserId.Value != me.userId)
                        return Ok(new { success = false, error = "Solo el agente asignado puede cerrar esta conversación." });
                }

                if (!IsOpen(meta.Status))
                    return Ok(new { success = false, error = "La conversación ya está cerrada." });

                // API: POST /close
                var payload = JsonSerializer.Serialize(new { ended_At = DateTime.UtcNow, reason = (req.Reason ?? "").Trim() });
                var resp = await http.PostAsync($"api/general/conversation/{req.ConversationId}/close",
                    new StringContent(payload, Encoding.UTF8, "application/json"));

                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    return Ok(new { success = false, error = body });

                // si usás autocierre, removemos timer
                if (_autoClose.TryRemove(req.ConversationId, out var cts))
                {
                    try { cts.Cancel(); cts.Dispose(); } catch { }
                }

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return Ok(new { success = false, error = ex.Message });
            }
        }

        // Compatibilidad: tu nombre viejo
        [HttpPost]
        public Task<IActionResult> UpdateConversationStatus([FromBody] UpdateConversationStatusDto req)
            => CloseConversation(req);

        // =========================
        // Attachment download
        // =========================
        [HttpGet]
        public async Task<IActionResult> Attachment(int id)
        {
            try
            {
                if (id <= 0) return NotFound();

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return NotFound(reason);

                var res = await http.GetAsync($"api/general/attachment/{id}/content");
                if (!res.IsSuccessStatusCode) return NotFound();

                var bytes = await res.Content.ReadAsByteArrayAsync();
                var contentType = res.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

                var fileName =
                    res.Content.Headers.ContentDisposition?.FileNameStar ??
                    res.Content.Headers.ContentDisposition?.FileName ??
                    $"file-{id}";

                return File(bytes, contentType, fileName);
            }
            catch
            {
                return NotFound();
            }
        }

        // =========================
        // =========================
        // Helpers de negocio (bloqueo/escritura)
        // =========================
        private static bool IsOpen(string? status)
            => string.Equals((status ?? "open").Trim(), "open", StringComparison.OrdinalIgnoreCase);

        private static bool CanWrite(ConversationMeta meta, (int userId, int profileId, bool isAdmin) me)
        {
            if (!IsOpen(meta.Status)) return false;

            // en espera: bloquea a agentes (admin sí puede si querés)
            if (meta.IsOnHold && !me.isAdmin) return false;

            // si está asignada:
            if (meta.AssignedUserId.HasValue && meta.AssignedUserId.Value > 0)
            {
                // admin siempre
                if (me.isAdmin) return true;
                // solo dueño
                return meta.AssignedUserId.Value == me.userId;
            }

            // libre: agentes deben tomarla antes (admin sí puede si querés)
            return me.isAdmin;
        }

        private static string BuildWriteBlockReason(ConversationMeta meta, (int userId, int profileId, bool isAdmin) me)
        {
            if (!IsOpen(meta.Status))
                return "La conversación está cerrada. No se puede enviar.";

            if (meta.IsOnHold && !me.isAdmin)
                return "La conversación está en espera. Debes reanudarla antes de enviar mensajes.";

            if (meta.AssignedUserId.HasValue && meta.AssignedUserId.Value > 0 && !me.isAdmin && meta.AssignedUserId.Value != me.userId)
                return "La conversación ya está asignada a otro agente.";

            if (!meta.AssignedUserId.HasValue || meta.AssignedUserId.Value == 0)
                return "Debes tomar la conversación antes de enviar mensajes.";

            return "No tienes permiso para enviar mensajes en esta conversación.";
        }

        // =========================
        // =========================
        // Helpers API Client
        // =========================
        private (HttpClient http, bool ok, string reason) CreateApiClient()
        {
            var apiBase =
                _cfg["Api:BaseUrl"]?.TrimEnd('/')
                ?? _cfg["ApiSettings:ApiBaseUrl"]?.TrimEnd('/');

            if (string.IsNullOrWhiteSpace(apiBase))
                return (null!, false, "Api:BaseUrl / ApiSettings:ApiBaseUrl vacío");

            var http = _httpFactory.CreateClient();
            http.BaseAddress = new Uri(apiBase + "/");

            var companyId = ResolveEmpresaId();

            http.DefaultRequestHeaders.Remove("X-Company-Id");
            http.DefaultRequestHeaders.Remove("X-Empresa-Id");
            http.DefaultRequestHeaders.Remove("X-Empresa");

            http.DefaultRequestHeaders.Add("X-Company-Id", companyId);
            http.DefaultRequestHeaders.Add("X-Empresa-Id", companyId);
            http.DefaultRequestHeaders.Add("X-Empresa", companyId);

            var rawToken =
                   HttpContext.Session.GetString("JWT_TOKEN")
                ?? HttpContext.Session.GetString("JwtToken")
                ?? User?.FindFirst("jwt")?.Value
                ?? Request.Cookies["JWT_TOKEN"];

            var token = CleanupToken(rawToken);

            var exp = GetJwtExpiry(token);
            if (IsExpiredOrNear(exp))
            {
                try
                {
                    HttpContext.Session.Remove("JWT_TOKEN");
                    HttpContext.Session.Remove("JwtToken");
                }
                catch { /* ignore */ }

                token = string.Empty;
            }

            http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(token)
                ? null
                : new AuthenticationHeaderValue("Bearer", token);

            http.DefaultRequestHeaders.Accept.Clear();
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            return (http, true, "");
        }

        private string ResolveEmpresaId()
        {
            var empSession = HttpContext.Session.GetString("COMPANY_ID");
            if (!string.IsNullOrWhiteSpace(empSession) && empSession != "0")
                return empSession;

            var empClaim = User?.FindFirst("empresa_id")?.Value
                        ?? User?.FindFirst("EmpresaId")?.Value
                        ?? User?.FindFirst("company_id")?.Value
                        ?? User?.FindFirst("CompanyId")?.Value;

            if (!string.IsNullOrWhiteSpace(empClaim) && empClaim != "0")
                return empClaim;

            var rawJwt = GetJwtToken();

            var empFromJwt =
                   TryGetClaimFromJwt(rawJwt, "empresa_id")
                ?? TryGetClaimFromJwt(rawJwt, "EmpresaId")
                ?? TryGetClaimFromJwt(rawJwt, "company_id")
                ?? TryGetClaimFromJwt(rawJwt, "CompanyId");

            if (!string.IsNullOrWhiteSpace(empFromJwt) && empFromJwt != "0")
                return empFromJwt;

            var cfgVal = _cfg["Api:CompanyId"];
            if (!string.IsNullOrWhiteSpace(cfgVal) && cfgVal != "0")
                return cfgVal;

            return "1";
        }

        private (int userId, int profileId, bool isAdmin) GetMe()
        {
            var user = HttpContext?.User;

            string? sId =
                user?.FindFirst("id")?.Value ??
                user?.FindFirst("user_id")?.Value ??
                user?.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                user?.FindFirst("sub")?.Value;

            int.TryParse(sId, out var userId);

            string? sProfile =
                user?.FindFirst("idProfile")?.Value ??
                user?.FindFirst("profile_id")?.Value ??
                user?.FindFirst("IdProfile")?.Value ??
                user?.FindFirst("ProfileId")?.Value;

            int.TryParse(sProfile, out var profileId);

            var role =
                user?.FindFirst("role")?.Value ??
                user?.FindFirst(ClaimTypes.Role)?.Value ??
                "";

            var isAdmin =
                profileId == 2 || profileId == 3 ||
                role.Equals("admin", StringComparison.OrdinalIgnoreCase) ||
                role.Equals("superadmin", StringComparison.OrdinalIgnoreCase);

            return (userId, profileId, isAdmin);
        }

        private string? GetJwtToken()
        {
            return HttpContext.Session.GetString("JWT_TOKEN")
                ?? HttpContext.Session.GetString("JwtToken")
                ?? Request.Cookies["JWT_TOKEN"];
        }

        private static string CleanupToken(string? t)
        {
            if (string.IsNullOrWhiteSpace(t)) return string.Empty;
            var s = t.Trim();
            if (s.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) s = s.Substring(7).Trim();
            if (s.StartsWith("\"") && s.EndsWith("\"")) s = s.Trim('\"');
            return s;
        }

        private static string? TryGetClaimFromJwt(string? jwt, string claimName)
        {
            if (string.IsNullOrWhiteSpace(jwt)) return null;
            try
            {
                var parts = jwt.Split('.');
                if (parts.Length < 2) return null;

                var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty(claimName, out var val))
                {
                    if (val.ValueKind == JsonValueKind.String) return val.GetString();
                    if (val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out var i)) return i.ToString();
                    return val.ToString();
                }

                foreach (var p in doc.RootElement.EnumerateObject())
                    if (string.Equals(p.Name, claimName, StringComparison.OrdinalIgnoreCase))
                        return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();

                return null;
            }
            catch { return null; }
        }

        private static DateTimeOffset? GetJwtExpiry(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;
            try
            {
                var parts = token.Split('.');
                if (parts.Length < 2) return null;

                var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                using var doc = JsonDocument.Parse(payloadJson);

                if (doc.RootElement.TryGetProperty("exp", out var expEl))
                {
                    long exp = expEl.ValueKind == JsonValueKind.String
                        ? long.Parse(expEl.GetString()!)
                        : expEl.GetInt64();

                    return DateTimeOffset.FromUnixTimeSeconds(exp);
                }
            }
            catch { /* ignore */ }

            return null;
        }

        private static bool IsExpiredOrNear(DateTimeOffset? exp, int skewSeconds = 60)
        {
            if (exp == null) return false;
            return DateTimeOffset.UtcNow >= exp.Value.AddSeconds(-skewSeconds);
        }

        private static byte[] Base64UrlDecode(string input)
        {
            string s = input.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }

        private sealed class ConversationMeta
        {
            public int Id { get; set; }
            public int ContactId { get; set; }
            public string Status { get; set; } = "open";
            public bool IsOnHold { get; set; }
            public string? OnHoldReason { get; set; }
            public int? AssignedUserId { get; set; }
            public DateTime? StartedAt { get; set; }
            public DateTime? LastActivityAt { get; set; }
        }

        private async Task<ConversationMeta?> GetConversationMetaAsync(HttpClient http, int conversationId)
        {
            try
            {
                var resp = await http.GetAsync($"api/general/conversation/{conversationId}");
                var raw = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) return null;

                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                // API suele devolver { exitoso, data }
                if (TryGetCaseInsensitive(root, "data", out var data) && data.ValueKind == JsonValueKind.Object)
                    root = data;

                var id = GetIntFlex(root, "id", "Id") ?? 0;
                if (id <= 0) return null;

                return new ConversationMeta
                {
                    Id = id,
                    ContactId = GetIntFlex(root, "contact_id", "ContactId", "contactId") ?? 0,
                    Status = GetStringFlex(root, "status", "Status") ?? "open",
                    IsOnHold = GetBoolFlex(root, "is_on_hold", "IsOnHold") ?? false,
                    OnHoldReason = GetStringFlex(root, "on_hold_reason", "OnHoldReason"),
                    AssignedUserId = GetIntFlex(root, "assigned_user_id", "AssignedUserId", "assignedUserId"),
                    StartedAt = GetDateFlex(root, "started_at", "StartedAt", "startedAt"),
                    LastActivityAt = GetDateFlex(root, "last_activity_at", "LastActivityAt", "lastActivityAt")
                };
            }
            catch
            {
                return null;
            }
        }

        private async Task<string?> ResolveContactPhoneAsync(HttpClient http, int contactId)
        {
            if (contactId <= 0) return null;

            try
            {
                var resC = await http.GetAsync("api/general/contact");
                if (!resC.IsSuccessStatusCode) return null;

                using var docC = JsonDocument.Parse(await resC.Content.ReadAsStringAsync());
                foreach (var c in ExtraerItems(docC.RootElement))
                {
                    var id = GetIntFlex(c, "id", "Id") ?? 0;
                    if (id == contactId)
                        return GetStringFlex(c, "phone_number", "phoneNumber", "PhoneNumber", "phone", "Phone");
                }
            }
            catch { /* ignore */ }

            return null;
        }

        private static async Task<(bool success, string? error, int? conversationId, bool justCreated)> SendTextViaApiAsync(HttpClient http, object payload)
        {
            var res = await http.PostAsJsonAsync("api/integraciones/whatsapp/send/text", payload);
            var body = await res.Content.ReadAsStringAsync();

            try
            {
                using var jd = JsonDocument.Parse(body);
                var root = jd.RootElement;

                bool ok = false;
                if (root.TryGetProperty("exitoso", out var ex))
                {
                    if (ex.ValueKind == JsonValueKind.True) ok = true;
                    else if (ex.ValueKind == JsonValueKind.String && bool.TryParse(ex.GetString(), out var b) && b) ok = true;
                }
                else if (root.TryGetProperty("success", out var sc))
                {
                    if (sc.ValueKind == JsonValueKind.True) ok = true;
                    else if (sc.ValueKind == JsonValueKind.String && bool.TryParse(sc.GetString(), out var b2) && b2) ok = true;
                }

                if (!res.IsSuccessStatusCode || !ok)
                {
                    string msg =
                        (root.TryGetProperty("mensaje", out var m1) && m1.ValueKind == JsonValueKind.String) ? m1.GetString()! :
                        (root.TryGetProperty("message", out var m2) && m2.ValueKind == JsonValueKind.String) ? m2.GetString()! :
                        (root.TryGetProperty("error", out var m3) && m3.ValueKind == JsonValueKind.String) ? m3.GetString()! :
                        body;

                    return (false, $"API {(int)res.StatusCode}: {msg}", null, false);
                }

                int? convId = null;
                bool just = false;

                if (root.TryGetProperty("conversacion_id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
                    convId = idEl.GetInt32();

                if (root.TryGetProperty("conversationId", out var idEl2) && idEl2.ValueKind == JsonValueKind.Number)
                    convId = idEl2.GetInt32();

                if (root.TryGetProperty("just_created", out var jcEl) && (jcEl.ValueKind == JsonValueKind.True || jcEl.ValueKind == JsonValueKind.False))
                    just = jcEl.GetBoolean();

                if (root.TryGetProperty("justCreated", out var jcEl2) && (jcEl2.ValueKind == JsonValueKind.True || jcEl2.ValueKind == JsonValueKind.False))
                    just = jcEl2.GetBoolean();

                return (true, null, convId, just);
            }
            catch
            {
                if (!res.IsSuccessStatusCode)
                    return (false, $"API {(int)res.StatusCode}: {body}", null, false);

                return (true, null, null, false);
            }
        }

        private static bool ExtractApiOk(string body)
        {
            try
            {
                using var jd = JsonDocument.Parse(body);
                var root = jd.RootElement;

                var b1 = GetBoolFlex(root, "exitoso", "success", "ok");
                if (b1.HasValue) return b1.Value;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string? ExtractApiError(string body)
        {
            try
            {
                using var jd = JsonDocument.Parse(body);
                var root = jd.RootElement;
                return GetStringFlex(root, "mensaje", "message", "error", "detalle", "detail");
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeAudioFileName(string? fileName, string mime)
        {
            var name = string.IsNullOrWhiteSpace(fileName) ? "audio-whatsapp" : Path.GetFileNameWithoutExtension(fileName);
            name = string.IsNullOrWhiteSpace(name) ? "audio-whatsapp" : name;

            var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
            if (ext == ".ogg" || ext == ".opus") return $"{name}{ext}";

            var m = (mime ?? "").ToLowerInvariant();
            if (m.Contains("ogg") || m.Contains("opus")) return $"{name}.ogg";
            if (m.Contains("mpeg")) return $"{name}.mp3";
            if (m.Contains("mp4") || m.Contains("aac")) return $"{name}.m4a";
            if (m.Contains("webm")) return $"{name}.webm";

            return $"{name}.ogg";
        }

        // ========= JSON utils =========
        private static IEnumerable<JsonElement> ExtraerItems(JsonElement root)
        {
            if (TryGetCaseInsensitive(root, "data", out var data))
            {
                if (TryGetCaseInsensitive(data, "$values", out var values) && values.ValueKind == JsonValueKind.Array)
                    return values.EnumerateArray().ToArray();
                if (data.ValueKind == JsonValueKind.Array) return data.EnumerateArray().ToArray();
            }

            if (TryGetCaseInsensitive(root, "$values", out var rvalues) && rvalues.ValueKind == JsonValueKind.Array)
                return rvalues.EnumerateArray().ToArray();

            if (root.ValueKind == JsonValueKind.Array) return root.EnumerateArray().ToArray();

            foreach (var p in root.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Array) return p.Value.EnumerateArray().ToArray();

            return Array.Empty<JsonElement>();
        }

        private static bool TryGetCaseInsensitive(JsonElement obj, string name, out JsonElement value)
        {
            if (obj.ValueKind != JsonValueKind.Object) { value = default; return false; }
            foreach (var p in obj.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return true; }
            value = default; return false;
        }

        private static string? GetStringFlex(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
                if (TryGetCaseInsensitive(obj, n, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString();
            return null;
        }

        private static int? GetIntFlex(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
            {
                if (TryGetCaseInsensitive(obj, n, out var v))
                {
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
                    if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out i)) return i;
                }
            }
            return null;
        }

        private static long? GetLongFlex(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
            {
                if (TryGetCaseInsensitive(obj, n, out var v))
                {
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l)) return l;
                    if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out l)) return l;
                }
            }
            return null;
        }

        private static bool? GetBoolFlex(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
            {
                if (TryGetCaseInsensitive(obj, n, out var v))
                {
                    if (v.ValueKind == JsonValueKind.True) return true;
                    if (v.ValueKind == JsonValueKind.False) return false;
                    if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b)) return b;
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i != 0;
                }
            }
            return null;
        }

        private static DateTime? GetDateFlex(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
            {
                if (TryGetCaseInsensitive(obj, n, out var v))
                {
                    if (v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(), out var dt)) return dt;
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var epoch))
                        return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
                }
            }
            return null;
        }

        private static string SoloDigitos(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s) if (char.IsDigit(ch)) sb.Append(ch);
            return sb.ToString();
        }

        private sealed class AttachmentInfo
        {
            public int Id { get; set; }
            public int MessageId { get; set; }
            public string FileName { get; set; } = string.Empty;
            public string MimeType { get; set; } = "application/octet-stream";
            public long? SizeBytes { get; set; }
        }
    }
}
