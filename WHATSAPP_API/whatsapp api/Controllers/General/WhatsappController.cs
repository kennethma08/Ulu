


//ELIMINAR

/*



using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Infrastructure.MultiTenancy;
using Whatsapp_API.Bussiness.Integraciones;
using Whatsapp_API.Data;
using Whatsapp_API.Helpers;
using Whatsapp_API.Models.Entidades.Mensajeria;
using Whatsapp_API.Models.Helpers;
using System.Text.Json;

namespace Whatsapp_API.Controllers.General
{
    [ApiController]
    [Route("api/general/[controller]")]
    public class WhatsappController : ControllerBase
    {
        private readonly WhatsappSender _sender;
        private readonly CorreoHelper _correo;
        private readonly MyDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<WhatsappController> _logger;
        private readonly TenantContext _tenant;

        public WhatsappController(
            WhatsappSender sender,
            CorreoHelper correo,
            MyDbContext db,
            IWebHostEnvironment env,
            ILogger<WhatsappController> logger,
            TenantContext tenant)
        {
            _sender = sender;
            _correo = correo;
            _db = db;
            _env = env;
            _logger = logger;
            _tenant = tenant;
        }



        private static bool IsStale(Conversacion c, DateTime nowUtc)
        {
            var last = c.LastActivityAt ?? c.StartedAt;
            return last <= nowUtc.AddHours(-23);
        }

        // IMPORTANTE: Filtra por empresa (global filters ya aplican), y status == "open"
        private async Task<Conversacion> EnsureOpenConversationAsync(int contactId)
        {
            var now = DateTime.UtcNow;
            var cid = _tenant.CompanyId ?? 0;

            var open = await _db.Conversaciones
                .Where(c => c.ContactId == contactId && c.Status == "open")
                .OrderByDescending(c => c.LastActivityAt ?? c.StartedAt)
                .FirstOrDefaultAsync();

            if (open == null || IsStale(open, now))
            {
                var conv = new Conversacion
                {
                    ContactId = contactId,
                    Status = "open",
                    StartedAt = now,
                    LastActivityAt = now,
                    CompanyId = cid
                };
                _db.Conversaciones.Add(conv);
                await _db.SaveChangesAsync();
                return conv;
            }

            return open;
        }


        public class SendMessageRequest
        {
            public string To { get; set; } = "";
            public string? Message { get; set; }
            public string? Type { get; set; } // "text" | "media"
            public bool IsTemplate { get; set; } = false;
            public string? TemplateName { get; set; }
            public string? LanguageCode { get; set; }
            public string? TemplateParams { get; set; }
        }

        public class SendFileRequest
        {
            [FromForm(Name = "file")]
            public IFormFile File { get; set; } = default!;

            [FromForm(Name = "to")]
            public string To { get; set; } = "";

            [FromForm(Name = "caption")]
            public string? Caption { get; set; }
        }

        private List<string> ParseTemplateParams(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
            raw = raw.Trim();
            if (raw.StartsWith("[")) { try { return JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>(); } catch { } }
            if (raw.Contains('|')) return raw.Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            if (raw.Contains(',')) return raw.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            return new List<string> { raw };
        }


        [HttpPost("send_message")]
        public async Task<IActionResult> SendMessage([FromForm] SendMessageRequest req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.To))
                    return BadRequest(new { status = "error", message = "Missing 'To' phone number" });

                var cid = _tenant.CompanyId ?? 0;

                var contacto = await _db.Contactos.FirstOrDefaultAsync(c => c.PhoneNumber == req.To);
                if (contacto == null)
                {
                    contacto = new Contacto { PhoneNumber = req.To, CreatedAt = DateTime.UtcNow, CompanyId = cid };
                    _db.Contactos.Add(contacto);
                    await _db.SaveChangesAsync();
                }

                var conv = await EnsureOpenConversationAsync(contacto.Id);

                var now = DateTime.UtcNow;
                var mensaje = new Mensaje
                {
                    ConversationId = conv.Id,
                    ContactId = contacto.Id,
                    Sender = "agent",
                    Message = req.Message,
                    Type = string.IsNullOrWhiteSpace(req.Type) ? "text" : req.Type,
                    SentAt = now,
                    CompanyId = cid
                };
                _db.Mensajes.Add(mensaje);
                conv.LastActivityAt = now;
                await _db.SaveChangesAsync();

                BooleanoDescriptivo sendResult;
                if (req.IsTemplate && !string.IsNullOrWhiteSpace(req.TemplateName))
                {
                    var bodyVars = ParseTemplateParams(req.TemplateParams);
                    sendResult = await _sender.SendTemplateAsync(req.To, req.TemplateName, req.LanguageCode ?? "en_US", bodyVars);
                }
                else
                {
                    sendResult = await _sender.SendTextAsync(req.To, req.Message ?? string.Empty);
                }

                return Ok(new
                {
                    status = sendResult.Exitoso ? "success" : "error",
                    conversation_id = conv.Id,
                    detail = sendResult.Mensaje,
                    code = sendResult.StatusCode
                });
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, new { req });
                _logger.LogError(ex, "SendMessage error");
                return StatusCode(500, new { status = "error", message = ex.Message });
            }
        }


        public class SendNoticeRequest
        {
            public int ConversationId { get; set; }
            public string To { get; set; } = "";
            public string? Message { get; set; }
        }

        [HttpPost("send_notice_no_open")]
        public async Task<IActionResult> SendNoticeNoOpen([FromForm] SendNoticeRequest req)
        {
            try
            {
                if (req.ConversationId <= 0 || string.IsNullOrWhiteSpace(req.To))
                    return BadRequest(new { status = "error", message = "Faltan datos" });

                var cid = _tenant.CompanyId ?? 0;

                var conv = await _db.Conversaciones.FirstOrDefaultAsync(c => c.Id == req.ConversationId);
                if (conv == null) return NotFound(new { status = "error", message = "Conversación no encontrada" });

                var contacto = await _db.Contactos.FirstOrDefaultAsync(c => c.PhoneNumber == req.To);
                var contactId = contacto?.Id ?? 0;

                var now = DateTime.UtcNow;
                var msg = new Mensaje
                {
                    ConversationId = conv.Id,
                    ContactId = contactId,
                    Sender = "agent",
                    Message = req.Message,
                    Type = "text",
                    SentAt = now,
                    CompanyId = cid
                };
                _db.Mensajes.Add(msg);
                conv.LastActivityAt = now;
                await _db.SaveChangesAsync();

                var result = await _sender.SendTextAsync(req.To, req.Message ?? string.Empty);
                return Ok(new { status = result.Exitoso ? "success" : "error" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "send_notice_no_open error");
                return StatusCode(500, new { status = "error", message = ex.Message });
            }
        }


        [HttpPost("send_file")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(100 * 1024 * 1024)]
        public async Task<IActionResult> SendFile([FromForm] SendFileRequest req)
        {
            var file = req.File;
            var to = req.To;
            var caption = req.Caption;
            try
            {
                if (file == null || string.IsNullOrWhiteSpace(to))
                    return BadRequest(new { status = "error", message = "Faltan datos" });

                var cid = _tenant.CompanyId ?? 0;

                var contacto = await _db.Contactos.FirstOrDefaultAsync(c => c.PhoneNumber == to);
                if (contacto == null)
                {
                    contacto = new Contacto { PhoneNumber = to, CreatedAt = DateTime.UtcNow, CompanyId = cid };
                    _db.Contactos.Add(contacto);
                    await _db.SaveChangesAsync();
                }

                var conv = await EnsureOpenConversationAsync(contacto.Id);

                var mensaje = new Mensaje
                {
                    ConversationId = conv.Id,
                    ContactId = contacto.Id,
                    Sender = "agent",
                    Message = caption,
                    Type = "media",
                    SentAt = DateTime.UtcNow,
                    CompanyId = cid
                };
                _db.Mensajes.Add(mensaje);
                await _db.SaveChangesAsync();

                byte[] data;
                using (var ms = new MemoryStream())
                {
                    await file.CopyToAsync(ms);
                    data = ms.ToArray();
                }
                var adjunto = new Adjunto
                {
                    MessageId = mensaje.Id,
                    FileName = file.FileName,
                    MimeType = file.ContentType,
                    Data = data,
                    UploadedAt = DateTime.UtcNow,
                    CompanyId = cid
                };
                _db.Adjuntos.Add(adjunto);
                await _db.SaveChangesAsync();

                var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + Path.GetExtension(file.FileName));
                await System.IO.File.WriteAllBytesAsync(tempPath, data);
                var fInfo = new System.IO.FileInfo(tempPath);
                var sendResult = await _sender.SendMediaAsync(to, fInfo, caption);
                try { System.IO.File.Delete(tempPath); } catch { }

                var mediaUrl = Url.Action(nameof(GetAttachment), "Whatsapp", new { id = adjunto.Id }, Request.Scheme) ?? "";

                return Ok(new { status = sendResult.Exitoso ? "success" : "error", url = mediaUrl, detail = sendResult.Mensaje, code = sendResult.StatusCode });
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, new { to });
                _logger.LogError(ex, "SendFile error");
                return StatusCode(500, new { status = "error", message = ex.Message });
            }
        }


        [HttpGet("attachment/{id}")]
        public async Task<IActionResult> GetAttachment(int id)
        {
            try
            {
                var adj = await _db.Adjuntos.FirstOrDefaultAsync(a => a.Id == id);
                if (adj == null || adj.Data == null) return NotFound();

                var fileName = string.IsNullOrWhiteSpace(adj.FileName) ? $"attachment_{adj.Id}" : adj.FileName;
                return File(adj.Data, adj.MimeType ?? "application/octet-stream", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAttachment error");
                return StatusCode(500, "Error interno");
            }
        }


        public class WebhookDto
        {
            public string? FromPhone { get; set; }
            public string? Text { get; set; }
        }

        [HttpPost("webhook/whatsapp")]
        public async Task<IActionResult> Inbound([FromBody] WebhookDto dto)
        {
            try
            {
                if (dto == null || string.IsNullOrWhiteSpace(dto.FromPhone))
                    return Ok();

                var phone = dto.FromPhone;
                var cid = _tenant.CompanyId ?? 0;

                var contact = await _db.Contactos.FirstOrDefaultAsync(c => c.PhoneNumber == phone);
                if (contact == null)
                {
                    contact = new Contacto { PhoneNumber = phone, CreatedAt = DateTime.UtcNow, CompanyId = cid };
                    _db.Add(contact);
                    await _db.SaveChangesAsync();
                }

                var conv = await EnsureOpenConversationAsync(contact.Id);

                var now = DateTime.UtcNow;
                var msg = new Mensaje
                {
                    ConversationId = conv.Id,
                    ContactId = contact.Id,
                    Sender = "contact",
                    Message = dto.Text,
                    Type = "text",
                    SentAt = now,
                    CompanyId = cid
                };
                _db.Add(msg);
                conv.LastActivityAt = now;
                await _db.SaveChangesAsync();

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Inbound error");
                return Ok();
            }
        }
    }
}

*/