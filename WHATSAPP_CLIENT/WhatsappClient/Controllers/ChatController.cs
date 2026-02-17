using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
    public class UpdateConversationStatusDto
    {
        public int ConversationId { get; set; }
        public string Status { get; set; } = "open";
        public int? ContactId { get; set; }
        public DateTime? StartedAt { get; set; }
        public string? Reason { get; set; }
        public bool? ReleaseAgent { get; set; }
    }

    public class AssignConversationDto
    {
        public int ConversationId { get; set; }
        public int ToUserId { get; set; }
        public string? Reason { get; set; }
    }

    public class HoldConversationDto
    {
        public int ConversationId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    [Authorize]
    [Route("Chat/[action]")]
    public class ChatController : Controller
    {
        private readonly IConfiguration _cfg;
        private readonly IHttpClientFactory _httpFactory;

        private static readonly ConcurrentDictionary<int, CancellationTokenSource> _autoClose = new();
        private static readonly TimeSpan AUTO_CLOSE_AFTER = TimeSpan.FromHours(23);

        private const int PROFILE_AGENT = 1;
        private const int PROFILE_ADMIN = 2;
        private const int PROFILE_SUPERADMIN = 3;

        public ChatController(IConfiguration cfg, IHttpClientFactory httpFactory)
        {
            _cfg = cfg;
            _httpFactory = httpFactory;
        }

        [HttpGet]
        [Route("", Name = "ChatRoot")]
        public IActionResult Index() => View();

        [HttpGet]
        public IActionResult Me()
        {
            var ctx = GetCurrentUserContext();
            return Ok(new
            {
                userId = ctx.userId,
                profileId = ctx.profileId,
                isAdmin = ctx.isAdmin,
                isAgent = ctx.isAgent
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetAgents()
        {
            try
            {
                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { agents = Array.Empty<object>(), error = reason });

                var ctx = GetCurrentUserContext();

                var resAgents = await http.GetAsync("api/seguridad/user/by-perfil-id/1");
                var rawAgents = await resAgents.Content.ReadAsStringAsync();
                if (!resAgents.IsSuccessStatusCode)
                    return Ok(new { agents = Array.Empty<object>(), error = "No se pudo obtener agentes" });

                var openCountByAgent = new Dictionary<int, int>();
                var waitingCountByAgent = new Dictionary<int, int>();

                try
                {
                    var resPanel = await http.GetAsync("api/general/conversation/panel");
                    if (resPanel.IsSuccessStatusCode)
                    {
                        using var docP = JsonDocument.Parse(await resPanel.Content.ReadAsStringAsync());
                        foreach (var c in ExtraerItems(docP.RootElement))
                        {
                            var status = NormalizeStatus(GetStringFlex(c, "status", "Status") ?? "open");
                            var assigned = GetIntFlex(c, "assigned_user_id", "AssignedUserId", "assignedUserId");
                            var isOnHold = GetBoolFlex(c, "is_on_hold", "IsOnHold") ?? false;

                            if (assigned.HasValue && assigned.Value > 0)
                            {
                                if (isOnHold || status == "waiting" || status == "on_hold")
                                    waitingCountByAgent[assigned.Value] = (waitingCountByAgent.TryGetValue(assigned.Value, out var w) ? w : 0) + 1;
                                else if (status == "open")
                                    openCountByAgent[assigned.Value] = (openCountByAgent.TryGetValue(assigned.Value, out var v) ? v : 0) + 1;
                            }
                        }
                    }
                }
                catch { }

                using var doc = JsonDocument.Parse(rawAgents);

                var agents = ExtraerItems(doc.RootElement)
                    .Select(it =>
                    {
                        var id = GetIntFlex(it, "id", "Id") ?? 0;
                        var name = GetStringFlex(it, "name", "Name", "fullName", "FullName", "username", "Username") ?? "";
                        openCountByAgent.TryGetValue(id, out var openCount);
                        waitingCountByAgent.TryGetValue(id, out var waitingCount);

                        return new
                        {
                            id,
                            name,
                            openCount,
                            waitingCount,
                            isMe = ctx.userId == id
                        };
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

        [HttpGet]
        public async Task<IActionResult> GetAllConversations()
        {
            try
            {
                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { conversations = Array.Empty<object>(), error = reason });

                var ctx = GetCurrentUserContext();

                string convRaw;
                HttpResponseMessage resPanel = await http.GetAsync("api/general/conversation/panel");
                convRaw = await resPanel.Content.ReadAsStringAsync();

                bool usedPanel = resPanel.IsSuccessStatusCode;

                if (!usedPanel)
                {
                    var resConv = await http.GetAsync("api/general/conversation");
                    convRaw = await resConv.Content.ReadAsStringAsync();

                    if (!resConv.IsSuccessStatusCode)
                        return Ok(new { conversations = Array.Empty<object>(), error = "No se pudo obtener conversaciones" });
                }

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

                var agentNameById = new Dictionary<int, string>();
                var resAgents = await http.GetAsync("api/seguridad/user/by-perfil-id/1");
                if (resAgents.IsSuccessStatusCode)
                {
                    using var adoc = JsonDocument.Parse(await resAgents.Content.ReadAsStringAsync());
                    foreach (var a in ExtraerItems(adoc.RootElement))
                    {
                        var id = GetIntFlex(a, "id", "Id") ?? 0;
                        if (id <= 0) continue;

                        var nm = GetStringFlex(a, "name", "Name", "fullName", "FullName", "username", "Username") ?? $"User {id}";
                        agentNameById[id] = nm;
                    }
                }

                using var docConv = JsonDocument.Parse(convRaw);

                var convsRaw = ExtraerItems(docConv.RootElement)
                    .Select(e => new
                    {
                        id = GetIntFlex(e, "id", "Id") ?? 0,
                        contactId = GetIntFlex(e, "contact_id", "ContactId", "contactId") ?? 0,
                        status = NormalizeStatus(GetStringFlex(e, "status", "Status") ?? "open"),
                        startedAt = GetDateFlex(e, "started_at", "StartedAt", "startedAt"),
                        lastActivityAt = GetDateFlex(e, "last_activity_at", "LastActivityAt", "lastActivityAt"),
                        agentRequestedAt = GetDateFlex(e, "agent_requested_at", "AgentRequestedAt", "agentRequestedAt"),
                        assignedUserId = GetIntFlex(e, "assigned_user_id", "AssignedUserId", "assignedUserId"),
                        isOnHold = GetBoolFlex(e, "is_on_hold", "IsOnHold", "isOnHold") ?? false,
                        onHoldReason = GetStringFlex(e, "on_hold_reason", "OnHoldReason", "onHoldReason")
                    })
                    .ToList();

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

                        var badge = GetUiBadge(x.status, x.assignedUserId, x.isOnHold);
                        var isMine = x.assignedUserId.HasValue && x.assignedUserId.Value == ctx.userId;

                        var canWrite = CanWrite(ctx, x.status, x.assignedUserId, x.isOnHold);
                        var canTake = CanTake(ctx, x.status, x.assignedUserId);

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

                            uiBadge = badge,               // "assigned" | "unassigned" | "waiting" | "closed"
                            isMine,
                            canWrite,
                            canTake,
                            isLockedForAgents = (x.assignedUserId.HasValue && x.assignedUserId.Value > 0)
                        };
                    })
                    .OrderByDescending(x => x.lastActivityAt ?? x.startedAt)
                    .ToList();

                return Ok(new { conversations = convs, me = new { ctx.userId, ctx.profileId, ctx.isAdmin, ctx.isAgent } });
            }
            catch (Exception ex)
            {
                return Ok(new { conversations = Array.Empty<object>(), error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetConversationMeta(int conversationId)
        {
            try
            {
                if (conversationId <= 0)
                    return Ok(new { ok = false, error = "conversationId inválido" });

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { ok = false, error = reason });

                var ctx = GetCurrentUserContext();

                var meta = await LoadConversationMetaAsync(http, conversationId);
                if (meta == null)
                    return Ok(new { ok = false, error = "No se encontró la conversación" });

                return Ok(new
                {
                    ok = true,
                    conversation = new
                    {
                        id = meta.Id,
                        status = meta.Status,
                        contactId = meta.ContactId,
                        startedAt = meta.StartedAt,
                        lastActivityAt = meta.LastActivityAt,
                        assignedUserId = meta.AssignedUserId,
                        assignedUserName = meta.AssignedUserName,
                        contactName = meta.ContactName,
                        contactPhone = meta.ContactPhone,
                        isOnHold = meta.IsOnHold,
                        onHoldReason = meta.OnHoldReason,
                        uiBadge = GetUiBadge(meta.Status, meta.AssignedUserId, meta.IsOnHold)
                    },
                    permissions = new
                    {
                        canWrite = CanWrite(ctx, meta.Status, meta.AssignedUserId, meta.IsOnHold),
                        canTake = CanTake(ctx, meta.Status, meta.AssignedUserId),
                        canAssign = ctx.isAdmin,
                        isMine = meta.AssignedUserId.HasValue && meta.AssignedUserId.Value == ctx.userId
                    },
                    me = new { ctx.userId, ctx.profileId, ctx.isAdmin, ctx.isAgent }
                });
            }
            catch (Exception ex)
            {
                return Ok(new { ok = false, error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetContactConversations(string phone)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(phone))
                    return Ok(new { conversations = Array.Empty<object>(), error = "phone requerido" });

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { conversations = Array.Empty<object>(), error = reason });

                var phoneDigits = SoloDigitos(phone);

                var resContact = await http.GetAsync("api/general/contact");
                if (!resContact.IsSuccessStatusCode)
                    return Ok(new { conversations = Array.Empty<object>(), error = "No se pudo obtener contactos" });

                using var docC = JsonDocument.Parse(await resContact.Content.ReadAsStringAsync());
                var contactos = ExtraerItems(docC.RootElement)
                    .Select(e => new
                    {
                        Id = GetIntFlex(e, "id", "Id") ?? 0,
                        PhoneDigits = SoloDigitos(GetStringFlex(e, "phone_number", "phoneNumber", "PhoneNumber", "phone", "Phone"))
                    })
                    .ToList();

                var contact = contactos.FirstOrDefault(c => c.PhoneDigits == phoneDigits);
                if (contact == null || contact.Id <= 0)
                    return Ok(new { conversations = Array.Empty<object>() });

                var resConv = await http.GetAsync("api/general/conversation");
                if (!resConv.IsSuccessStatusCode)
                    return Ok(new { conversations = Array.Empty<object>(), error = "No se pudo obtener conversaciones" });

                using var docConv = JsonDocument.Parse(await resConv.Content.ReadAsStringAsync());
                var convs = ExtraerItems(docConv.RootElement)
                    .Where(e => (GetIntFlex(e, "contact_id", "ContactId", "contactId") ?? 0) == contact.Id)
                    .Select(e => new
                    {
                        id = GetIntFlex(e, "id", "Id") ?? 0,
                        contactId = GetIntFlex(e, "contact_id", "ContactId", "contactId") ?? 0,
                        status = NormalizeStatus(GetStringFlex(e, "status", "Status") ?? "open"),
                        startedAt = GetDateFlex(e, "started_at", "StartedAt", "startedAt"),
                        lastActivityAt = GetDateFlex(e, "last_activity_at", "LastActivityAt", "lastActivityAt"),
                        totalMessages = GetIntFlex(e, "total_messages", "TotalMessages") ?? 0,
                        greetingSent = GetBoolFlex(e, "greeting_sent", "Greeting_Sent", "GreetingSent") ?? false,
                        agentRequestedAt = GetDateFlex(e, "agent_requested_at", "AgentRequestedAt", "agentRequestedAt"),
                        isOnHold = GetBoolFlex(e, "is_on_hold", "IsOnHold", "isOnHold") ?? false,
                        onHoldReason = GetStringFlex(e, "on_hold_reason", "OnHoldReason", "onHoldReason")
                    })
                    .Where(x => x.agentRequestedAt != null)
                    .OrderByDescending(x => x.lastActivityAt ?? x.startedAt)
                    .ToList();

                return Ok(new { conversations = convs });
            }
            catch (Exception ex)
            {
                return Ok(new { conversations = Array.Empty<object>(), error = ex.Message });
            }
        }

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
                catch { }

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

        [HttpPost]
        public async Task<IActionResult> TakeConversation([FromBody] JsonElement body)
        {
            try
            {
                var conversationId = GetIntFlex(body, "conversationId", "ConversationId") ?? 0;
                if (conversationId <= 0) return Ok(new { ok = false, error = "conversationId inválido" });

                var ctx = GetCurrentUserContext();
                if (ctx.userId <= 0) return Ok(new { ok = false, error = "Usuario inválido" });

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { ok = false, error = reason });

                var meta = await LoadConversationMetaAsync(http, conversationId);
                if (meta == null) return Ok(new { ok = false, error = "No se encontró la conversación" });

                if (meta.Status == "closed")
                    return Ok(new { ok = false, error = "La conversación está cerrada." });

                if (!CanTake(ctx, meta.Status, meta.AssignedUserId))
                    return Ok(new { ok = false, error = "No tienes permisos para tomar esta conversación." });

                if (meta.AssignedUserId.HasValue && meta.AssignedUserId.Value > 0 && meta.AssignedUserId.Value != ctx.userId && !ctx.isAdmin)
                    return Ok(new { ok = false, error = "Esta conversación ya está tomada por otro agente." });

                if (!(meta.AssignedUserId.HasValue && meta.AssignedUserId.Value == ctx.userId))
                {
                    var assignedOk = await AssignApiAsync(http, conversationId, ctx.userId);
                    if (!assignedOk) return Ok(new { ok = false, error = "No se pudo asignar la conversación." });
                }

                // Al tomarla, si estaba en espera, la quitamos (Opcional según tu flujo, pero recomendable)
                if (meta.IsOnHold)
                {
                    await ResumeApiAsync(http, conversationId);
                }

                return Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return Ok(new { ok = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> HoldConversation([FromBody] HoldConversationDto req)
        {
            try
            {
                if (req == null || req.ConversationId <= 0)
                    return Ok(new { ok = false, error = "payload inválido" });

                if (string.IsNullOrWhiteSpace(req.Reason))
                    return Ok(new { ok = false, error = "reason requerido" });

                var ctx = GetCurrentUserContext();
                if (ctx.userId <= 0) return Ok(new { ok = false, error = "Usuario inválido" });

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { ok = false, error = reason });

                var meta = await LoadConversationMetaAsync(http, req.ConversationId);
                if (meta == null) return Ok(new { ok = false, error = "No se encontró la conversación" });

                if (meta.Status == "closed")
                    return Ok(new { ok = false, error = "La conversación está cerrada." });

                if (!ctx.isAdmin)
                {
                    if (ctx.profileId != PROFILE_AGENT)
                        return Ok(new { ok = false, error = "No tienes permisos para poner en espera." });

                    if (!meta.AssignedUserId.HasValue || meta.AssignedUserId.Value <= 0)
                    {
                        var assignedOk = await AssignApiAsync(http, req.ConversationId, ctx.userId);
                        if (!assignedOk) return Ok(new { ok = false, error = "No se pudo asignar la conversación." });
                        meta.AssignedUserId = ctx.userId;
                    }
                    else if (meta.AssignedUserId.Value != ctx.userId)
                    {
                        return Ok(new { ok = false, error = "Esta conversación está tomada por otro agente." });
                    }
                }

                // Usar el nuevo endpoint de la API Central
                var payload = JsonSerializer.Serialize(new { reason = req.Reason });
                var resp = await http.PostAsync($"api/general/conversation/{req.ConversationId}/hold",
                           new StringContent(payload, Encoding.UTF8, "application/json"));

                if (!resp.IsSuccessStatusCode)
                    return Ok(new { ok = false, error = "No se pudo poner en espera." });

                // Si deseas soltarla automáticamente al ponerla en espera para que otro la tome (Comenta si no es necesario)
                // await ReleaseApiAsync(http, req.ConversationId);

                return Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return Ok(new { ok = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ResumeConversation([FromBody] JsonElement body)
        {
            try
            {
                var conversationId = GetIntFlex(body, "conversationId", "ConversationId") ?? 0;
                if (conversationId <= 0) return Ok(new { ok = false, error = "conversationId inválido" });

                var ctx = GetCurrentUserContext();
                if (ctx.userId <= 0) return Ok(new { ok = false, error = "Usuario inválido" });

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { ok = false, error = reason });

                var meta = await LoadConversationMetaAsync(http, conversationId);
                if (meta == null) return Ok(new { ok = false, error = "No se encontró la conversación" });

                if (!ctx.isAdmin && meta.AssignedUserId.HasValue && meta.AssignedUserId.Value != ctx.userId)
                    return Ok(new { ok = false, error = "Esta conversación pertenece a otro agente." });

                var resOk = await ResumeApiAsync(http, conversationId);
                if (!resOk) return Ok(new { ok = false, error = "No se pudo reanudar la conversación." });

                return Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return Ok(new { ok = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> TransferConversation([FromBody] AssignConversationDto req)
        {
            try
            {
                if (req == null || req.ConversationId <= 0 || req.ToUserId <= 0)
                    return Ok(new { ok = false, error = "payload inválido" });

                var ctx = GetCurrentUserContext();
                if (ctx.userId <= 0) return Ok(new { ok = false, error = "Usuario inválido" });

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { ok = false, error = reason });

                var meta = await LoadConversationMetaAsync(http, req.ConversationId);
                if (meta == null) return Ok(new { ok = false, error = "No se encontró la conversación" });

                if (meta.Status == "closed")
                    return Ok(new { ok = false, error = "La conversación está cerrada." });

                if (!ctx.isAdmin)
                {
                    if (ctx.profileId != PROFILE_AGENT)
                        return Ok(new { ok = false, error = "No tienes permisos para transferir." });

                    if (!meta.AssignedUserId.HasValue || meta.AssignedUserId.Value != ctx.userId)
                        return Ok(new { ok = false, error = "Solo el agente asignado puede transferir esta conversación." });
                }

                // Transferir usando el endpoint central
                var assignedOk2 = await AssignApiAsync(http, req.ConversationId, req.ToUserId);
                if (!assignedOk2) return Ok(new { ok = false, error = "No se pudo transferir la conversación." });

                return Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return Ok(new { ok = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage([FromBody] JsonElement body)
        {
            try
            {
                if (!body.TryGetProperty("conversationId", out var convEl) || convEl.ValueKind != JsonValueKind.Number)
                    return Ok(new { success = false, error = "conversationId requerido" });

                var conversationId = convEl.GetInt32();
                var contactId = body.TryGetProperty("contactId", out var cidEl) && cidEl.ValueKind == JsonValueKind.Number ? cidEl.GetInt32() : 0;
                var contactPhone = body.TryGetProperty("contactPhone", out var phEl) && phEl.ValueKind == JsonValueKind.String ? phEl.GetString() : null;
                var message = body.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String ? msgEl.GetString() : null;

                if (string.IsNullOrWhiteSpace(message))
                    return Ok(new { success = false, error = "message requerido" });

                var ctx = GetCurrentUserContext();

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { success = false, error = reason });

                var meta = await LoadConversationMetaAsync(http, conversationId);
                if (meta == null)
                    return Ok(new { success = false, error = "No se encontró la conversación." });

                if (meta.Status != "open")
                    return Ok(new { success = false, error = "La conversación no está abierta. No se puede enviar." });

                if (!ctx.isAdmin)
                {
                    if (ctx.profileId != PROFILE_AGENT)
                        return Ok(new { success = false, error = "No autorizado." });

                    if (!meta.AssignedUserId.HasValue || meta.AssignedUserId.Value <= 0)
                    {
                        var assignedOk = await AssignApiAsync(http, conversationId, ctx.userId);
                        if (!assignedOk) return Ok(new { success = false, error = "No se pudo asignar la conversación." });
                        meta.AssignedUserId = ctx.userId;
                    }
                    else if (meta.AssignedUserId.Value != ctx.userId)
                    {
                        return Ok(new { success = false, error = "Esta conversación ya está tomada por otro agente." });
                    }
                }

                if (string.IsNullOrWhiteSpace(contactPhone) && contactId > 0)
                {
                    var resC = await http.GetAsync("api/general/contact");
                    if (resC.IsSuccessStatusCode)
                    {
                        using var docC = JsonDocument.Parse(await resC.Content.ReadAsStringAsync());
                        foreach (var c in ExtraerItems(docC.RootElement))
                        {
                            var id = GetIntFlex(c, "id", "Id") ?? 0;
                            if (id == contactId)
                            {
                                contactPhone = GetStringFlex(c, "phone_number", "phoneNumber", "PhoneNumber", "phone", "Phone");
                                break;
                            }
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(contactPhone))
                    return Ok(new { success = false, error = "No se pudo resolver el teléfono del contacto" });

                var apiRes = await SendTextViaApiAsync(http, new
                {
                    Contact_Id = contactId > 0 ? (int?)contactId : null,
                    Conversation_Id = conversationId,
                    To_Phone = contactPhone,
                    Text = message,
                    Create_If_Not_Exists = false,
                    Log = true
                });

                if (!apiRes.success)
                    return Ok(new { success = false, error = apiRes.error });

                return Ok(new { success = true, conversationId = apiRes.conversationId, justCreated = apiRes.justCreated });
            }
            catch (Exception ex)
            {
                return Ok(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        [Consumes("multipart/form-data")]
        [Route("~/Chat/SendAudio")]
        [Route("~/api/integraciones/whatsapp/agent/audio")]
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

                var ctx = GetCurrentUserContext();

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { success = false, error = reason });

                var meta = await LoadConversationMetaAsync(http, conversationId);
                if (meta == null)
                    return Ok(new { success = false, error = "No se encontró la conversación." });

                if (meta.Status != "open")
                    return Ok(new { success = false, error = "La conversación no está abierta. No se puede enviar audio." });

                if (!ctx.isAdmin)
                {
                    if (ctx.profileId != PROFILE_AGENT)
                        return Ok(new { success = false, error = "No autorizado." });

                    if (!meta.AssignedUserId.HasValue || meta.AssignedUserId.Value <= 0)
                    {
                        var assignedOk = await AssignApiAsync(http, conversationId, ctx.userId);
                        if (!assignedOk) return Ok(new { success = false, error = "No se pudo asignar la conversación." });
                        meta.AssignedUserId = ctx.userId;
                    }
                    else if (meta.AssignedUserId.Value != ctx.userId)
                    {
                        return Ok(new { success = false, error = "Esta conversación ya está tomada por otro agente." });
                    }
                }

                var finalContactId = (contactId.HasValue && contactId.Value > 0)
                    ? contactId.Value
                    : (meta.ContactId ?? 0);

                if (finalContactId <= 0)
                    return Ok(new { success = false, error = "No se pudo resolver contactId para enviar el audio." });

                if (string.IsNullOrWhiteSpace(contactPhone))
                {
                    var resC = await http.GetAsync("api/general/contact");
                    if (resC.IsSuccessStatusCode)
                    {
                        using var docC = JsonDocument.Parse(await resC.Content.ReadAsStringAsync());
                        foreach (var c in ExtraerItems(docC.RootElement))
                        {
                            var id = GetIntFlex(c, "id", "Id") ?? 0;
                            if (id == finalContactId)
                            {
                                contactPhone = GetStringFlex(c, "phone_number", "phoneNumber", "PhoneNumber", "phone", "Phone");
                                break;
                            }
                        }
                    }
                }

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

        [HttpGet]
        public async Task<IActionResult> Attachment(int id)
        {
            try
            {
                if (id <= 0)
                    return NotFound();

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return NotFound(reason);

                var res = await http.GetAsync($"api/general/attachment/{id}/content");
                if (!res.IsSuccessStatusCode)
                    return NotFound();

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

        [HttpPost]
        public async Task<IActionResult> AssignConversation([FromBody] JsonElement body)
        {
            try
            {
                var conversationId = GetIntFlex(body, "conversationId", "ConversationId") ?? 0;
                var toUserId = GetIntFlex(body, "toUserId", "ToUserId") ?? 0;

                if (conversationId <= 0) return Ok(new { ok = false, error = "conversationId inválido" });
                if (toUserId <= 0) return Ok(new { ok = false, error = "toUserId inválido" });

                var ctx = GetCurrentUserContext();
                if (!ctx.isAdmin)
                    return Ok(new { ok = false, error = "No autorizado." });

                var (http, ok, reasonClient) = CreateApiClient();
                if (!ok) return Ok(new { ok = false, error = reasonClient });

                var okAssign = await AssignApiAsync(http, conversationId, toUserId);
                if (!okAssign)
                    return Ok(new { ok = false, error = "No se pudo asignar la conversación" });

                return Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return Ok(new { ok = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ReleaseConversation([FromBody] JsonElement body)
        {
            try
            {
                var conversationId = GetIntFlex(body, "conversationId", "ConversationId") ?? 0;
                if (conversationId <= 0) return Ok(new { ok = false, error = "conversationId inválido" });

                var ctx = GetCurrentUserContext();
                if (!ctx.isAdmin)
                    return Ok(new { ok = false, error = "No autorizado." });

                var (http, ok, reason) = CreateApiClient();
                if (!ok) return Ok(new { ok = false, error = reason });

                var okRel = await ReleaseApiAsync(http, conversationId);
                if (!okRel)
                    return Ok(new { ok = false, error = "No se pudo liberar la conversación" });

                return Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return Ok(new { ok = false, error = ex.Message });
            }
        }

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
                catch { }
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

                var payload = parts[1];
                var json = Encoding.UTF8.GetString(Base64UrlDecode(payload));

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(claimName, out var val))
                {
                    if (val.ValueKind == JsonValueKind.String)
                        return val.GetString();
                    if (val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out var i))
                        return i.ToString();
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
            catch { }
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

        private (int userId, int profileId, bool isAdmin, bool isAgent) GetCurrentUserContext()
        {
            var token = GetJwtToken();
            var uid = TryGetClaimFromJwt(token, "sub")
                   ?? TryGetClaimFromJwt(token, "user_id")
                   ?? TryGetClaimFromJwt(token, "id")
                   ?? TryGetClaimFromJwt(token, "nameid");

            var pid = TryGetClaimFromJwt(token, "idProfile")
                   ?? TryGetClaimFromJwt(token, "profile_id")
                   ?? TryGetClaimFromJwt(token, "ProfileId");

            int.TryParse(uid, out var userId);
            int.TryParse(pid, out var profileId);

            var role = TryGetClaimFromJwt(token, "role") ?? "";

            var isAdmin =
                profileId == PROFILE_ADMIN || profileId == PROFILE_SUPERADMIN ||
                role.Equals("admin", StringComparison.OrdinalIgnoreCase) ||
                role.Equals("superadmin", StringComparison.OrdinalIgnoreCase);

            var isAgent = profileId == PROFILE_AGENT;

            return (userId, profileId, isAdmin, isAgent);
        }

        private static bool CanWrite((int userId, int profileId, bool isAdmin, bool isAgent) ctx, string status, int? assignedUserId, bool isOnHold)
        {
            status = NormalizeStatus(status);
            if (status != "open") return false;
            // Opcional: si está en espera bloqueas la escritura. Descomenta la siguiente linea si lo deseas:
            // if (isOnHold) return false; 
            if (ctx.isAdmin) return true;
            if (ctx.profileId == PROFILE_AGENT && assignedUserId.HasValue && assignedUserId.Value == ctx.userId) return true;
            return false;
        }

        private static bool CanTake((int userId, int profileId, bool isAdmin, bool isAgent) ctx, string status, int? assignedUserId)
        {
            status = NormalizeStatus(status);
            if (status == "closed") return false;
            if (ctx.isAdmin) return true;
            if (ctx.profileId != PROFILE_AGENT) return false;
            if (!assignedUserId.HasValue || assignedUserId.Value <= 0) return true;
            return assignedUserId.Value == ctx.userId;
        }

        private static string GetUiBadge(string status, int? assignedUserId, bool isOnHold)
        {
            status = NormalizeStatus(status);

            if (status == "closed") return "closed";
            if (isOnHold || status == "waiting") return "waiting"; // NARANJA
            if (assignedUserId.HasValue && assignedUserId.Value > 0) return "assigned"; // ROJO
            return "unassigned"; // VERDE
        }

        private static string NormalizeStatus(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "open";
            s = s.Trim().ToLowerInvariant();

            if (s == "onhold" || s == "on_hold" || s == "hold") return "waiting";
            if (s == "waiting" || s == "wait" || s == "pending") return "waiting";
            if (s == "open" || s == "abierta" || s == "abierto") return "open";
            if (s == "closed" || s == "cerrada" || s == "cerrado") return "closed";

            return s;
        }

        private sealed class ConversationMeta
        {
            public int Id { get; set; }
            public int? ContactId { get; set; }
            public string Status { get; set; } = "open";
            public DateTime? StartedAt { get; set; }
            public DateTime? LastActivityAt { get; set; }
            public int? AssignedUserId { get; set; }
            public string? AssignedUserName { get; set; }
            public string? ContactName { get; set; }
            public string? ContactPhone { get; set; }

            // NUEVO
            public bool IsOnHold { get; set; }
            public string? OnHoldReason { get; set; }
        }

        private async Task<ConversationMeta?> LoadConversationMetaAsync(HttpClient http, int conversationId)
        {
            JsonElement? found = null;
            string rawPanel = "";

            try
            {
                var resPanel = await http.GetAsync("api/general/conversation/panel");
                if (resPanel.IsSuccessStatusCode)
                {
                    rawPanel = await resPanel.Content.ReadAsStringAsync();
                    using var docP = JsonDocument.Parse(rawPanel);
                    foreach (var c in ExtraerItems(docP.RootElement))
                    {
                        var id = GetIntFlex(c, "id", "Id") ?? 0;
                        if (id == conversationId) { found = c; break; }
                    }
                }
            }
            catch { }

            if (found == null)
            {
                try
                {
                    var resConv = await http.GetAsync("api/general/conversation");
                    if (!resConv.IsSuccessStatusCode) return null;

                    using var docC = JsonDocument.Parse(await resConv.Content.ReadAsStringAsync());
                    foreach (var c in ExtraerItems(docC.RootElement))
                    {
                        var id = GetIntFlex(c, "id", "Id") ?? 0;
                        if (id == conversationId) { found = c; break; }
                    }
                }
                catch
                {
                    return null;
                }
            }

            if (found == null) return null;

            var el = found.Value;

            var meta = new ConversationMeta
            {
                Id = GetIntFlex(el, "id", "Id") ?? conversationId,
                ContactId = GetIntFlex(el, "contact_id", "ContactId", "contactId"),
                Status = NormalizeStatus(GetStringFlex(el, "status", "Status") ?? "open"),
                StartedAt = GetDateFlex(el, "started_at", "StartedAt", "startedAt"),
                LastActivityAt = GetDateFlex(el, "last_activity_at", "LastActivityAt", "lastActivityAt"),
                AssignedUserId = GetIntFlex(el, "assigned_user_id", "AssignedUserId", "assignedUserId"),
                IsOnHold = GetBoolFlex(el, "is_on_hold", "IsOnHold") ?? false,
                OnHoldReason = GetStringFlex(el, "on_hold_reason", "OnHoldReason")
            };

            if (meta.ContactId.HasValue && meta.ContactId.Value > 0)
            {
                try
                {
                    var resContact = await http.GetAsync("api/general/contact");
                    if (resContact.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(await resContact.Content.ReadAsStringAsync());
                        foreach (var c in ExtraerItems(doc.RootElement))
                        {
                            var id = GetIntFlex(c, "id", "Id") ?? 0;
                            if (id == meta.ContactId.Value)
                            {
                                meta.ContactName = GetStringFlex(c, "name", "Name", "nombre", "Nombre", "fullName", "FullName");
                                meta.ContactPhone = GetStringFlex(c, "phone_number", "phoneNumber", "PhoneNumber", "phone", "Phone", "telefono", "Telefono");
                                break;
                            }
                        }
                    }
                }
                catch { }
            }

            if (meta.AssignedUserId.HasValue && meta.AssignedUserId.Value > 0)
            {
                try
                {
                    var resAgents = await http.GetAsync("api/seguridad/user/by-perfil-id/1");
                    if (resAgents.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(await resAgents.Content.ReadAsStringAsync());
                        foreach (var a in ExtraerItems(doc.RootElement))
                        {
                            var id = GetIntFlex(a, "id", "Id") ?? 0;
                            if (id == meta.AssignedUserId.Value)
                            {
                                meta.AssignedUserName = GetStringFlex(a, "name", "Name", "fullName", "FullName", "username", "Username");
                                break;
                            }
                        }
                    }
                }
                catch { }
            }

            if (meta.StartedAt == null)
            {
                try
                {
                    var resConv = await http.GetAsync("api/general/conversation");
                    if (resConv.IsSuccessStatusCode)
                    {
                        using var docC = JsonDocument.Parse(await resConv.Content.ReadAsStringAsync());
                        foreach (var c in ExtraerItems(docC.RootElement))
                        {
                            var id = GetIntFlex(c, "id", "Id") ?? 0;
                            if (id == conversationId)
                            {
                                meta.StartedAt = GetDateFlex(c, "started_at", "StartedAt", "startedAt") ?? meta.StartedAt;
                                break;
                            }
                        }
                    }
                }
                catch { }
            }

            return meta;
        }

        private static async Task<bool> AssignApiAsync(HttpClient http, int conversationId, int toUserId)
        {
            try
            {
                var payload = JsonSerializer.Serialize(new { toUserId });
                var resp = await http.PostAsync($"api/general/conversation/{conversationId}/assign",
                    new StringContent(payload, Encoding.UTF8, "application/json"));
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> ReleaseApiAsync(HttpClient http, int conversationId)
        {
            try
            {
                var resp = await http.PostAsync($"api/general/conversation/{conversationId}/release",
                    new StringContent("{}", Encoding.UTF8, "application/json"));
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> ResumeApiAsync(HttpClient http, int conversationId)
        {
            try
            {
                var resp = await http.PostAsync($"api/general/conversation/{conversationId}/resume",
                    new StringContent("{}", Encoding.UTF8, "application/json"));
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
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

                if (root.TryGetProperty("just_created", out var jcEl) &&
                    (jcEl.ValueKind == JsonValueKind.True || jcEl.ValueKind == JsonValueKind.False))
                    just = jcEl.GetBoolean();

                if (root.TryGetProperty("justCreated", out var jcEl2) &&
                    (jcEl2.ValueKind == JsonValueKind.True || jcEl2.ValueKind == JsonValueKind.False))
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