using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Whatsapp_API.BotFlows.Core;
using Whatsapp_API.Business.General;
using Whatsapp_API.Business.Integrations;
using Whatsapp_API.Data;
using Whatsapp_API.Helpers;
using Whatsapp_API.Infrastructure.MultiTenancy;
using Whatsapp_API.Models.Entities.Messaging;
using Whatsapp_API.Models.Helpers;

namespace Whatsapp_API.Controllers.General
{
    [ApiController]
    [Route("webhook/whatsapp/{pni?}")]
    [Authorize]
    public class WhatsAppWebhookController : ControllerBase
    {
        private static readonly ConcurrentDictionary<int, CancellationTokenSource> _autoClose = new();
        private static readonly TimeSpan AUTO_CLOSE_AFTER = TimeSpan.FromHours(23);
        private const string LOG_CAT = "whatsapp-webhook";

        // WhatsApp Cloud API: audio max 16MB
        private const long WA_MAX_MEDIA_BYTES = 16L * 1024L * 1024L;

        private readonly MyDbContext _db;
        private readonly TenantContext _tenant;
        private readonly ContactBus _contactBus;
        private readonly ConversationBus _conversationBus;
        private readonly MessageBus _messageBus;
        private readonly WhatsappSender _sender;
        private readonly IConfiguration _cfg;
        private readonly ILogger<WhatsAppWebhookController> _log;
        private readonly IFlowRouter _router;
        private readonly IntegrationBus _integrationBus;

        public WhatsAppWebhookController(
            MyDbContext db,
            TenantContext tenant,
            ContactBus contactBus,
            ConversationBus conversationBus,
            MessageBus messageBus,
            WhatsappSender sender,
            IConfiguration cfg,
            ILogger<WhatsAppWebhookController> log,
            IFlowRouter router,
            IntegrationBus integrationBus)
        {
            _db = db;
            _tenant = tenant;
            _contactBus = contactBus;
            _conversationBus = conversationBus;
            _messageBus = messageBus;
            _sender = sender;
            _cfg = cfg;
            _log = log;
            _router = router;
            _integrationBus = integrationBus;
        }

        // =========================
        // VERIFY (GET)
        // =========================
        [AllowAnonymous]
        [HttpGet]
        public IActionResult Verify(
            [FromRoute] string? pni,
            [FromQuery(Name = "hub.mode")] string? mode,
            [FromQuery(Name = "hub.verify_token")] string? token,
            [FromQuery(Name = "hub.challenge")] string? challenge)
        {
            SimpleFileLogger.Log(
                LOG_CAT,
                "VERIFY-IN",
                $"mode={mode} pni={pni ?? "null"} token={(string.IsNullOrEmpty(token) ? "null" : "***")} challenge={(string.IsNullOrEmpty(challenge) ? "null" : "present")}"
            );

            if (mode == "subscribe" &&
                !string.IsNullOrEmpty(token) &&
                !string.IsNullOrEmpty(challenge) &&
                !string.IsNullOrWhiteSpace(pni))
            {
                var integ = _db.Integrations
                    .AsNoTracking()
                    .FirstOrDefault(i => i.IsActive && i.PhoneNumberId == pni);

                var ok = integ != null && string.Equals(integ.VerifyTokenHash, token);

                SimpleFileLogger.Log(
                    LOG_CAT,
                    "VERIFY-LOOKUP",
                    $"found={integ != null} match={ok} eid={integ?.CompanyId.ToString() ?? "null"}"
                );

                if (ok)
                    return Content(challenge, "text/plain", Encoding.UTF8);
            }

            SimpleFileLogger.Log(LOG_CAT, "VERIFY-FORBID", $"pni={pni ?? "null"}");
            return Forbid();
        }

        // =========================
        // WEBHOOK INBOUND (POST)
        // =========================
        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> Receive([FromRoute] string? pni)
        {
            try
            {
                Request.EnableBuffering();
                string rawBody;
                using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true))
                {
                    rawBody = await reader.ReadToEndAsync();
                    Request.Body.Position = 0;
                }

                if (string.IsNullOrWhiteSpace(rawBody))
                {
                    SimpleFileLogger.Log(LOG_CAT, "INBOUND-EMPTY", $"url-pni={pni ?? "null"}");
                    return Ok();
                }

                SimpleFileLogger.LogJson(LOG_CAT, "INBOUND-BODY", rawBody);

                using (var doc = JsonDocument.Parse(rawBody))
                {
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("entry", out var entryArr) ||
                        entryArr.ValueKind != JsonValueKind.Array)
                        return Ok();

                    var defaultLang = _cfg["WhatsApp:TemplateLang"] ?? "es";
                    var fallbacks = _cfg.GetSection("WhatsApp:TemplateFallbacks").Get<string[]>() ?? new[]
                    {
                        "es_ES","es_MX","es","es_CO","es_AR"
                    };

                    var langsToTry = new List<string> { defaultLang };
                    langsToTry.AddRange(fallbacks.Where(l =>
                        !string.Equals(l, defaultLang, StringComparison.OrdinalIgnoreCase)));

                    var tplCierre = _cfg["WhatsApp:Templates:Cierre"];

                    foreach (var entry in entryArr.EnumerateArray())
                    {
                        if (!entry.TryGetProperty("changes", out var changes) ||
                            changes.ValueKind != JsonValueKind.Array)
                            continue;

                        foreach (var change in changes.EnumerateArray())
                        {
                            if (!change.TryGetProperty("value", out var value) ||
                                value.ValueKind != JsonValueKind.Object)
                                continue;

                            // ignorar statuses
                            if (value.TryGetProperty("statuses", out _))
                                continue;

                            // metadata.phone_number_id
                            string? metaPni = null;
                            if (value.TryGetProperty("metadata", out var meta) &&
                                meta.ValueKind == JsonValueKind.Object &&
                                meta.TryGetProperty("phone_number_id", out var pniJson) &&
                                pniJson.ValueKind == JsonValueKind.String)
                            {
                                metaPni = pniJson.GetString();
                            }

                            if (string.IsNullOrWhiteSpace(metaPni))
                            {
                                SimpleFileLogger.Log(LOG_CAT, "NO-METADATA-PNI", $"url-pni={pni ?? "null"}");
                                continue;
                            }

                            var integ = _db.Integrations
                                .AsNoTracking()
                                .FirstOrDefault(i => i.IsActive && i.PhoneNumberId == metaPni);

                            if (integ == null)
                            {
                                SimpleFileLogger.Log(LOG_CAT, "INTEGRATION-NOTFOUND", $"meta-pni={metaPni}");
                                continue;
                            }

                            _tenant.CompanyId = integ.CompanyId;
                            HttpContext.Items["COMPANY_ID"] = integ.CompanyId;

                            SimpleFileLogger.Log(
                                LOG_CAT,
                                "TENANT-SET",
                                $"eid={integ.CompanyId} phone_id={metaPni}"
                            );

                            // displayName opcional
                            string? displayName = null;
                            if (value.TryGetProperty("contacts", out var contacts) &&
                                contacts.ValueKind == JsonValueKind.Array)
                            {
                                var first = contacts.EnumerateArray().FirstOrDefault();
                                if (first.ValueKind == JsonValueKind.Object &&
                                    first.TryGetProperty("profile", out var profile) &&
                                    profile.TryGetProperty("name", out var nm))
                                {
                                    displayName = nm.GetString();
                                }
                            }

                            if (!value.TryGetProperty("messages", out var messages) ||
                                messages.ValueKind != JsonValueKind.Array)
                                continue;

                            foreach (var m in messages.EnumerateArray())
                            {
                                var from = m.TryGetProperty("from", out var fromProp)
                                    ? fromProp.GetString()
                                    : null;
                                if (string.IsNullOrWhiteSpace(from)) continue;

                                var phone = from!;
                                var msgUtc = ExtraerWaTimestampUtc(m) ?? DateTime.UtcNow;
                                var name = string.IsNullOrWhiteSpace(displayName) ? phone : displayName;

                                // CONTACTO
                                var cByPhone = _contactBus.FindByPhone(phone);
                                Contact contact;
                                bool welcomeAlready;

                                if (!cByPhone.Exitoso || cByPhone.Data == null)
                                {
                                    contact = new Contact
                                    {
                                        Name = name,
                                        PhoneNumber = phone,
                                        CreatedAt = msgUtc,
                                        LastMessageAt = msgUtc,
                                        Status = "active",
                                        WelcomeSent = false
                                    };

                                    var cr = _contactBus.Create(contact);

                                    SimpleFileLogger.Log(
                                        LOG_CAT,
                                        "CONTACT-CREATE",
                                        $"ok={cr.Exitoso} phone={phone} eid({_tenant.CompanyId})"
                                    );

                                    cByPhone = _contactBus.FindByPhone(phone);
                                    if (!cByPhone.Exitoso || cByPhone.Data == null)
                                        continue;

                                    contact = cByPhone.Data;
                                    welcomeAlready = false;
                                }
                                else
                                {
                                    contact = cByPhone.Data;
                                    contact.Name = name ?? contact.Name;
                                    contact.LastMessageAt = msgUtc;
                                    contact.Status = "active";
                                    _contactBus.Update(contact);
                                    welcomeAlready = contact.WelcomeSent;
                                }

                                // CONVERSACIÓN
                                var ensured = _conversationBus.EnsureOpenForIncoming(contact.Id);
                                if (!ensured.Exitoso || ensured.Data == null)
                                {
                                    SimpleFileLogger.Log(
                                        LOG_CAT,
                                        "CONV-ENSURE-FAIL",
                                        $"contactId={contact.Id} eid({_tenant.CompanyId}) msg={ensured.Mensaje}"
                                    );
                                    continue;
                                }

                                var convId = ensured.Data.Id;
                                var justCreated = ensured.StatusCode == 201;

                                // MENSAJE
                                var (msgType, msgText) = ExtractMessageTypeAndText(m);

                                var cm = _messageBus.Create(new Message
                                {
                                    ConversationId = convId,
                                    ContactId = contact.Id,
                                    Sender = "contact",
                                    Messages = msgText,
                                    Type = msgType,
                                    SentAt = msgUtc
                                });

                                SimpleFileLogger.Log(
                                    LOG_CAT,
                                    "MSG-CREATE",
                                    $"ok={cm.Exitoso} convId={convId} contactId={contact.Id} eid({_tenant.CompanyId})"
                                );

                                // buscar message recién creado para Attachment
                                Message? savedMsg = null;
                                if (cm.Exitoso)
                                {
                                    try
                                    {
                                        var eid = _tenant.CompanyId;
                                        savedMsg = _db.Messages
                                            .OrderByDescending(x => x.Id)
                                            .FirstOrDefault(x =>
                                                x.ConversationId == convId &&
                                                x.ContactId == contact.Id &&
                                                x.CompanyId == eid &&
                                                x.SentAt == msgUtc &&
                                                x.Type == msgType &&
                                                x.Messages == msgText);
                                    }
                                    catch { }
                                }

                                if (savedMsg != null)
                                {
                                    await TryCreateAttachmentFromIncomingAsync(m, savedMsg);
                                }

                                // touch + autocierre
                                var convUpd = _conversationBus.Find(convId);
                                if (convUpd.Exitoso && convUpd.Data != null)
                                {
                                    convUpd.Data.LastActivityAt = DateTime.UtcNow;
                                    if (!string.Equals(convUpd.Data.Status, "closed", StringComparison.OrdinalIgnoreCase))
                                    {
                                        convUpd.Data.Status = "open";
                                    }
                                    else
                                    {
                                        convUpd.Data.EndedAt ??= DateTime.UtcNow;
                                    }
                                    _conversationBus.Update(convUpd.Data);
                                }

                                ScheduleAutoClose(convId, phone, tplCierre, langsToTry);

                                // Flow
                                try
                                {
                                    var flowInput = new FlowInput
                                    {
                                        CompanyId = _tenant.CompanyId,
                                        ContactId = contact.Id,
                                        ConversationId = convId,
                                        PhoneE164 = phone,
                                        MessageType = msgType,
                                        MessageText = msgText,
                                        ReceivedAtUtc = msgUtc,
                                        JustCreated = justCreated
                                    };

                                    await _router.RouteAsync(flowInput);
                                }
                                catch (Exception fx)
                                {
                                    SimpleFileLogger.LogJson(LOG_CAT, "FLOW-ERR", fx.ToString());
                                }
                            }
                        }
                    }
                }

                return Ok();
            }
            catch (Exception ex)
            {
                SimpleFileLogger.LogJson(LOG_CAT, "ERROR", ex.ToString());
                _log.LogError(ex, "Error en webhook/whatsapp");
                return Ok();
            }
        }

        // =========================================================
        // AUDIO DESDE PANEL /api/integraciones/whatsapp/agent/audio
        // RE-ENCODE A M4A/AAC-LC MONO PARA EVITAR "scrutiny failed"
        // SIN FFMpegCore: usa ffmpeg.exe (NuGet o config)
        // =========================================================
        [HttpPost("~/api/integraciones/whatsapp/agent/audio")]
        [Consumes("multipart/form-data")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> SendAudioFromAgent(
            [FromForm] IFormFile? file,
            [FromForm] int? conversationId,
            [FromForm] int? contactId,
            [FromForm] string? toPhone)
        {
            try
            {
                int convId = conversationId ?? 0;
                if (convId <= 0)
                {
                    var qConv = Request.Query["conversationId"].FirstOrDefault();
                    if (int.TryParse(qConv, out var fromQuery) && fromQuery > 0)
                        convId = fromQuery;
                }

                if (convId <= 0)
                    return BadRequest(new { success = false, error = "conversationId es requerido." });

                if (file == null || file.Length == 0)
                    return BadRequest(new { success = false, error = "No se recibió archivo de audio." });

                var companyId = ResolveCompanyId();
                if (companyId <= 0)
                    return BadRequest(new { success = false, error = "CompanyId/Tenant no resuelto." });

                _tenant.CompanyId = companyId;
                HttpContext.Items["COMPANY_ID"] = companyId;

                var conv = await _db.Conversations
                    .FirstOrDefaultAsync(c => c.Id == convId && c.CompanyId == companyId);

                if (conv == null)
                    return NotFound(new { success = false, error = "Conversación no encontrada." });

                if (!string.Equals(conv.Status, "open", StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new { success = false, error = "La conversación no está abierta." });

                int finalContactId = contactId ?? 0;
                if (finalContactId <= 0)
                {
                    var qContact = Request.Query["contactId"].FirstOrDefault();
                    if (int.TryParse(qContact, out var cFromQuery) && cFromQuery > 0)
                        finalContactId = cFromQuery;
                }

                if (finalContactId <= 0)
                    finalContactId = conv.ContactId;

                if (finalContactId <= 0)
                    return BadRequest(new { success = false, error = "contactId inválido." });

                var contact = await _db.Contacts
                    .FirstOrDefaultAsync(c => c.Id == finalContactId && c.CompanyId == companyId);

                if (contact == null)
                    return NotFound(new { success = false, error = "Contacto no encontrado." });

                var destPhone = SoloDigitos(toPhone);
                if (string.IsNullOrWhiteSpace(destPhone))
                    destPhone = SoloDigitos(contact.PhoneNumber);

                if (string.IsNullOrWhiteSpace(destPhone))
                    return BadRequest(new
                    {
                        success = false,
                        error = "No se pudo resolver el teléfono destino (toPhone/contact.PhoneNumber)."
                    });

                // Leemos bytes
                byte[] audioBytes;
                await using (var ms = new MemoryStream())
                {
                    await file.CopyToAsync(ms);
                    audioBytes = ms.ToArray();
                }

                // Nombre/mime de entrada (para extensión temporal)
                var incomingMime = SanitizeMime(file.ContentType);
                var incomingName = string.IsNullOrWhiteSpace(file.FileName)
                    ? $"audio_{convId}{ExtFromMime(incomingMime)}"
                    : file.FileName.Trim();

                // Convertir SIEMPRE a M4A/AAC-LC MONO
                var convResult = await ConvertAnyAudioToM4aAacLcAsync(audioBytes, incomingName, incomingMime);
                if (!convResult.ok)
                {
                    SimpleFileLogger.LogJson(LOG_CAT, "AUDIO-CONVERT-FAIL", convResult.error ?? "unknown");
                    return BadRequest(new
                    {
                        success = false,
                        error =
                            "No se pudo convertir el audio a un formato compatible (M4A/AAC). " +
                            "Instale un NuGet que incluya ffmpeg (ej: FFMpegInstaller.Windows.x64) o configure FFMPEG:Path. " +
                            $"Detalle: {convResult.error}"
                    });
                }

                audioBytes = convResult.bytes!;
                incomingMime = "audio/mp4";
                incomingName = convResult.fileName!;

                if (audioBytes.LongLength > WA_MAX_MEDIA_BYTES)
                {
                    return BadRequest(new { success = false, error = "El audio supera 16MB (límite de WhatsApp)." });
                }

                // Enviar a WhatsApp
                DescriptiveBoolean wa;
                await using (var stream = new MemoryStream(audioBytes))
                {
                    wa = await _sender.SendAudioAsync(destPhone, stream, incomingName, incomingMime);
                }

                if (!wa.Exitoso)
                {
                    SimpleFileLogger.LogJson(LOG_CAT, "AGENT-AUDIO-META-ERR", wa.Mensaje ?? "unknown");
                    var status = wa.StatusCode != 0 ? wa.StatusCode : 500;
                    return StatusCode(status, new { success = false, error = wa.Mensaje });
                }

                // Guardar mensaje + attachment (YA CONVERTIDO)
                var utcNow = DateTime.UtcNow;

                var msg = new Message
                {
                    CompanyId = companyId,
                    ConversationId = conv.Id,
                    ContactId = finalContactId,
                    Sender = "agent",
                    Type = "audio",
                    Messages = "[audio]",
                    SentAt = utcNow
                };
                _db.Messages.Add(msg);
                await _db.SaveChangesAsync();

                var att = new Attachment
                {
                    CompanyId = companyId,
                    MessageId = msg.Id,
                    FileName = incomingName,
                    MimeType = incomingMime,
                    SizeBytes = audioBytes.LongLength,
                    Data = audioBytes,
                    StorageProvider = "whatsapp",
                    StoragePath = null,
                    WhatsappMediaId = null,
                    UploadedAt = utcNow
                };
                _db.Attachments.Add(att);

                conv.LastActivityAt = utcNow;
                _db.Conversations.Update(conv);

                await _db.SaveChangesAsync();

                SimpleFileLogger.Log(
                    LOG_CAT,
                    "AGENT-AUDIO-SENT",
                    $"ok=1 convId={conv.Id} contactId={contact.Id} eid={companyId} mime={att.MimeType} size={att.SizeBytes}"
                );

                return Ok(new
                {
                    success = true,
                    conversationId = conv.Id,
                    messageId = msg.Id,
                    attachmentId = att.Id
                });
            }
            catch (Exception ex)
            {
                SimpleFileLogger.LogJson(LOG_CAT, "AGENT-AUDIO-ERR", ex.ToString());
                return StatusCode(500, new { success = false, error = "Error interno al enviar el audio." });
            }
        }

        // =========================
        // INBOUND: crear attachment
        // =========================
        private async Task TryCreateAttachmentFromIncomingAsync(JsonElement m, Message msgEntity)
        {
            try
            {
                if (!m.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String)
                    return;

                var type = t.GetString() ?? "";
                string mediaType;
                JsonElement mediaObj;

                switch (type)
                {
                    case "audio":
                        mediaType = "audio";
                        if (!m.TryGetProperty("audio", out mediaObj) || mediaObj.ValueKind != JsonValueKind.Object)
                            return;
                        break;

                    case "image":
                        mediaType = "image";
                        if (!m.TryGetProperty("image", out mediaObj) || mediaObj.ValueKind != JsonValueKind.Object)
                            return;
                        break;

                    case "video":
                        mediaType = "video";
                        if (!m.TryGetProperty("video", out mediaObj) || mediaObj.ValueKind != JsonValueKind.Object)
                            return;
                        break;

                    case "document":
                        mediaType = "document";
                        if (!m.TryGetProperty("document", out mediaObj) || mediaObj.ValueKind != JsonValueKind.Object)
                            return;
                        break;

                    default:
                        return;
                }

                if (!mediaObj.TryGetProperty("id", out var idProp) ||
                    idProp.ValueKind != JsonValueKind.String)
                    return;

                var mediaId = idProp.GetString();
                if (string.IsNullOrWhiteSpace(mediaId)) return;

                string? mimeType = null;
                if (mediaObj.TryGetProperty("mime_type", out var mtProp) &&
                    mtProp.ValueKind == JsonValueKind.String)
                {
                    mimeType = mtProp.GetString();
                }

                string? fileName = null;
                if (mediaObj.TryGetProperty("filename", out var fnProp) &&
                    fnProp.ValueKind == JsonValueKind.String)
                {
                    fileName = fnProp.GetString();
                }

                long? fileSize = null;
                if (mediaObj.TryGetProperty("file_size", out var fsProp))
                {
                    if (fsProp.ValueKind == JsonValueKind.Number && fsProp.TryGetInt64(out var n))
                        fileSize = n;
                    else if (fsProp.ValueKind == JsonValueKind.String &&
                             long.TryParse(fsProp.GetString(), out var n2))
                        fileSize = n2;
                }

                var companyId = msgEntity.CompanyId > 0
                    ? msgEntity.CompanyId
                    : _tenant.CompanyId;

                byte[]? data = null;
                string? realMime = null;

                var mediaResult = await DownloadMediaFromMetaAsync(mediaId!);
                if (mediaResult.data != null && mediaResult.data.Length > 0)
                {
                    data = mediaResult.data;
                    realMime = mediaResult.mime;
                    fileSize = mediaResult.data.Length;
                }

                var att = new Attachment
                {
                    MessageId = msgEntity.Id,
                    CompanyId = companyId,
                    FileName = fileName ?? $"{mediaType}_{mediaId}",
                    MimeType = SanitizeMime(realMime ?? mimeType ?? "application/octet-stream"),
                    SizeBytes = fileSize,
                    Data = data,
                    StorageProvider = "whatsapp",
                    StoragePath = mediaId,
                    WhatsappMediaId = mediaId,
                    UploadedAt = DateTime.UtcNow
                };

                _db.Attachments.Add(att);
                _db.SaveChanges();

                SimpleFileLogger.Log(
                    LOG_CAT,
                    "ATTACHMENT-CREATE",
                    $"mid={msgEntity.Id} eid={companyId} type={mediaType} mediaId={mediaId} hasData={(data != null ? "1" : "0")}"
                );
            }
            catch (Exception ex)
            {
                SimpleFileLogger.LogJson(LOG_CAT, "ATTACHMENT-ERR", ex.ToString());
            }
        }

        // =========================
        // MEDIA DOWNLOAD
        // =========================
        private async Task<(byte[]? data, string? mime)> DownloadMediaFromMetaAsync(string mediaId)
        {
            try
            {
                var (okConf, token, apiBaseUrl, apiVersion, phoneId, errConf) =
                    _integrationBus.GetDecryptedForSend();

                if (!okConf || string.IsNullOrWhiteSpace(token))
                {
                    SimpleFileLogger.Log(
                        LOG_CAT,
                        "MEDIA-NO-TOKEN",
                        errConf ?? "No hay integración activa o token no configurado (BD)."
                    );
                    return (null, null);
                }

                var baseUrl = BuildGraphBaseUrl(apiBaseUrl, apiVersion);

                using var http = new HttpClient();
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var metaResp = await http.GetAsync($"{baseUrl}/{mediaId}");
                var metaBody = await metaResp.Content.ReadAsStringAsync();

                if (!metaResp.IsSuccessStatusCode)
                {
                    SimpleFileLogger.LogJson(
                        LOG_CAT,
                        "MEDIA-META-ERR",
                        $"status={(int)metaResp.StatusCode} body={metaBody}"
                    );
                    return (null, null);
                }

                string? url = null;
                string? mime = null;

                using (var metaDoc = JsonDocument.Parse(metaBody))
                {
                    var root = metaDoc.RootElement;
                    if (root.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String)
                        url = urlEl.GetString();
                    if (root.TryGetProperty("mime_type", out var mtEl) && mtEl.ValueKind == JsonValueKind.String)
                        mime = mtEl.GetString();
                }

                if (string.IsNullOrWhiteSpace(url))
                {
                    SimpleFileLogger.Log(LOG_CAT, "MEDIA-META-NOURL", $"mediaId={mediaId}");
                    return (null, null);
                }

                var binResp = await http.GetAsync(url);
                if (!binResp.IsSuccessStatusCode)
                {
                    var txt = await binResp.Content.ReadAsStringAsync();
                    SimpleFileLogger.LogJson(
                        LOG_CAT,
                        "MEDIA-DOWN-ERR",
                        $"status={(int)binResp.StatusCode} body={txt}"
                    );
                    return (null, null);
                }

                var bytes = await binResp.Content.ReadAsByteArrayAsync();
                return (bytes, mime);
            }
            catch (Exception ex)
            {
                SimpleFileLogger.LogJson(LOG_CAT, "MEDIA-DOWN-EX", ex.ToString());
                return (null, null);
            }
        }

        // =========================
        // AUDIO CONVERSION (ffmpeg)
        // =========================
        private async Task<(bool ok, byte[]? bytes, string? fileName, string? error)> ConvertAnyAudioToM4aAacLcAsync(
            byte[] inputBytes,
            string originalName,
            string? originalMime)
        {
            // 1) Resolver ffmpeg
            var ffmpeg = ResolveFfmpegExePath();
            if (string.IsNullOrWhiteSpace(ffmpeg))
            {
                return (false, null, null,
                    "ffmpeg no encontrado. Configure FFMPEG:Path o use un NuGet que incluya ffmpeg (ej: FFMpegInstaller.Windows.x64).");
            }

            // 2) Crear temporales
            var inExt = Path.GetExtension(originalName);
            if (string.IsNullOrWhiteSpace(inExt))
            {
                inExt = ExtFromMime(originalMime);
            }

            var tmpIn = Path.Combine(Path.GetTempPath(), $"wa_in_{Guid.NewGuid():N}{inExt}");
            var tmpOut = Path.Combine(Path.GetTempPath(), $"wa_out_{Guid.NewGuid():N}.m4a");

            try
            {
                await System.IO.File.WriteAllBytesAsync(tmpIn, inputBytes);

                // 3) ffmpeg re-encode: M4A (MP4 container) + AAC-LC + mono + 48k + 64k
                // Esto evita el "scrutiny failed" típico del 131053.
                var args = $"-y -i \"{tmpIn}\" -vn -ac 1 -ar 48000 -c:a aac -profile:a aac_low -b:a 64k -movflags +faststart -f mp4 \"{tmpOut}\"";

                var psi = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var p = new Process { StartInfo = psi };
                var sbErr = new StringBuilder();
                var sbOut = new StringBuilder();

                p.OutputDataReceived += (_, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };

                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                // timeout duro para no colgar el request
                var finished = await Task.Run(() => p.WaitForExit(20000));
                if (!finished)
                {
                    try { p.Kill(true); } catch { }
                    return (false, null, null, "ffmpeg timeout (20s).");
                }

                if (p.ExitCode != 0 || !System.IO.File.Exists(tmpOut))
                {
                    var detail = sbErr.Length > 0 ? sbErr.ToString() : sbOut.ToString();
                    if (string.IsNullOrWhiteSpace(detail)) detail = $"ffmpeg exitCode={p.ExitCode}";
                    return (false, null, null, detail.Trim());
                }

                var outBytes = await System.IO.File.ReadAllBytesAsync(tmpOut);
                if (outBytes == null || outBytes.Length == 0)
                    return (false, null, null, "ffmpeg generó un archivo vacío.");

                var baseName = Path.GetFileNameWithoutExtension(originalName);
                if (string.IsNullOrWhiteSpace(baseName)) baseName = "audio-whatsapp";
                var outName = baseName + ".m4a";

                return (true, outBytes, outName, null);
            }
            catch (Exception ex)
            {
                return (false, null, null, ex.Message);
            }
            finally
            {
                try { if (System.IO.File.Exists(tmpIn)) System.IO.File.Delete(tmpIn); } catch { }
                try { if (System.IO.File.Exists(tmpOut)) System.IO.File.Delete(tmpOut); } catch { }
            }
        }

        private string? ResolveFfmpegExePath()
        {
            // 1) appsettings: FFMPEG:Path
            var cfgPath = _cfg["FFMPEG:Path"];
            if (!string.IsNullOrWhiteSpace(cfgPath) && System.IO.File.Exists(cfgPath))
                return cfgPath;

            // 2) output base dir + búsqueda recursiva (NuGet suele copiar ahí)
            var baseDir = AppContext.BaseDirectory;
            var isWin = OperatingSystem.IsWindows();
            var exeName = isWin ? "ffmpeg.exe" : "ffmpeg";

            // directo en root
            var direct = Path.Combine(baseDir, exeName);
            if (System.IO.File.Exists(direct))
                return direct;

            // carpetas comunes
            var commonDirs = new[]
            {
                baseDir,
                Path.Combine(baseDir, "ffmpeg"),
                Path.Combine(baseDir, "ffmpeg-bin"),
                Path.Combine(baseDir, "tools"),
                Path.Combine(baseDir, "runtimes"),
            };

            foreach (var d in commonDirs)
            {
                try
                {
                    if (!Directory.Exists(d)) continue;
                    var hit = Directory.EnumerateFiles(d, exeName, SearchOption.AllDirectories)
                        .FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(hit) && System.IO.File.Exists(hit))
                        return hit;
                }
                catch { }
            }

            // 3) PATH (si existe)
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = isWin ? "where" : "which",
                    Arguments = isWin ? "ffmpeg" : "ffmpeg",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                if (p != null)
                {
                    var outTxt = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(1500);
                    var first = (outTxt ?? "")
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault();

                    if (!string.IsNullOrWhiteSpace(first) && System.IO.File.Exists(first))
                        return first;
                }
            }
            catch { }

            return null;
        }

        private static string ExtFromMime(string? mime)
        {
            var m = (mime ?? "").Trim().ToLowerInvariant();
            var semi = m.IndexOf(';');
            if (semi > 0) m = m.Substring(0, semi);

            return m switch
            {
                "audio/webm" => ".webm",
                "audio/ogg" => ".ogg",
                "audio/mpeg" => ".mp3",
                "audio/mp3" => ".mp3",
                "audio/aac" => ".aac",
                "audio/amr" => ".amr",
                "audio/mp4" => ".m4a",
                _ => ".bin"
            };
        }

        // =========================
        // HELPERS
        // =========================
        private int ResolveCompanyId()
        {
            try
            {
                if (_tenant.CompanyId > 0)
                    return _tenant.CompanyId;

                if (Request.Headers.TryGetValue("X-Company-Id", out var h1) &&
                    int.TryParse(h1.FirstOrDefault(), out var a) && a > 0)
                    return a;

                if (Request.Headers.TryGetValue("X-Empresa-Id", out var h2) &&
                    int.TryParse(h2.FirstOrDefault(), out var b) && b > 0)
                    return b;

                if (Request.Headers.TryGetValue("X-Empresa", out var h3) &&
                    int.TryParse(h3.FirstOrDefault(), out var c) && c > 0)
                    return c;

                if (HttpContext.Items.TryGetValue("COMPANY_ID", out var obj) &&
                    obj != null &&
                    int.TryParse(obj.ToString(), out var d) && d > 0)
                    return d;
            }
            catch { }

            return 0;
        }

        private static string SoloDigitos(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                if (char.IsDigit(ch)) sb.Append(ch);
            return sb.ToString();
        }

        private static string SanitizeMime(string? mime)
        {
            if (string.IsNullOrWhiteSpace(mime)) return "audio/ogg";
            var s = mime.Trim();
            var semi = s.IndexOf(';');
            if (semi > 0) s = s.Substring(0, semi);
            return string.IsNullOrWhiteSpace(s) ? "audio/ogg" : s;
        }

        private static DateTime? ExtraerWaTimestampUtc(JsonElement m)
        {
            if (m.TryGetProperty("timestamp", out var tsProp) &&
                tsProp.ValueKind == JsonValueKind.String)
            {
                if (long.TryParse(tsProp.GetString(), out var epoch))
                    return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
            }
            return null;
        }

        private static (string type, string text) ExtractMessageTypeAndText(JsonElement m)
        {
            try
            {
                if (m.TryGetProperty("type", out var t) &&
                    t.ValueKind == JsonValueKind.String)
                {
                    var type = t.GetString() ?? "text";
                    switch (type)
                    {
                        case "text":
                            if (m.TryGetProperty("text", out var txt) &&
                                txt.TryGetProperty("body", out var body) &&
                                body.ValueKind == JsonValueKind.String)
                                return ("text", body.GetString() ?? "");
                            return ("text", "");

                        case "image":
                            string caption = "";
                            if (m.TryGetProperty("image", out var img) &&
                                img.TryGetProperty("caption", out var cap) &&
                                cap.ValueKind == JsonValueKind.String)
                                caption = cap.GetString() ?? "";
                            return ("image", string.IsNullOrWhiteSpace(caption) ? "[imagen]" : caption);

                        case "audio":
                            return ("audio", "[audio]");

                        case "document":
                            string docName = "";
                            if (m.TryGetProperty("document", out var doc) &&
                                doc.TryGetProperty("filename", out var fn) &&
                                fn.ValueKind == JsonValueKind.String)
                                docName = fn.GetString() ?? "";
                            return ("document", string.IsNullOrWhiteSpace(docName) ? "[documento]" : docName);

                        case "sticker":
                            return ("sticker", "[sticker]");

                        case "video":
                            return ("video", "[video]");

                        case "location":
                            return ("location", "[ubicación]");

                        case "button":
                            if (m.TryGetProperty("button", out var btn) &&
                                btn.TryGetProperty("text", out var btxt) &&
                                btxt.ValueKind == JsonValueKind.String)
                                return ("text", btxt.GetString() ?? "");
                            return ("text", "");

                        default:
                            return (type, $"[{type}]");
                    }
                }
            }
            catch { }

            return ("text", "");
        }

        private static string BuildGraphBaseUrl(string? apiBaseUrl, string? apiVersion)
        {
            var root = string.IsNullOrWhiteSpace(apiBaseUrl)
                ? "https://graph.facebook.com"
                : apiBaseUrl.TrimEnd('/');

            var version = string.IsNullOrWhiteSpace(apiVersion)
                ? "v20.0"
                : apiVersion.Trim('/');

            if (root.EndsWith("/" + version, StringComparison.OrdinalIgnoreCase))
                return root;

            return $"{root}/{version}";
        }

        private void ScheduleAutoClose(
            int conversationId,
            string toPhone,
            string? tplCierre,
            IEnumerable<string> langsToTry)
        {
            try
            {
                if (_autoClose.TryRemove(conversationId, out var old))
                {
                    try { old.Cancel(); old.Dispose(); } catch { }
                }

                var cts = new CancellationTokenSource();
                _autoClose[conversationId] = cts;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(AUTO_CLOSE_AFTER, cts.Token);
                        if (cts.IsCancellationRequested) return;

                        var found = _conversationBus.Find(conversationId);
                        if (found.Exitoso && found.Data != null)
                        {
                            found.Data.Status = "closed";
                            found.Data.LastActivityAt = DateTime.UtcNow;
                            found.Data.EndedAt ??= DateTime.UtcNow;
                            _conversationBus.Update(found.Data);
                        }

                        if (!string.IsNullOrWhiteSpace(tplCierre))
                            await _sender.SendTemplateWithFallbackAsync(toPhone, tplCierre!, langsToTry, null);
                    }
                    catch { }
                    finally
                    {
                        _autoClose.TryRemove(conversationId, out _);
                        try { cts.Dispose(); } catch { }
                    }
                });
            }
            catch { }
        }
    }
}
