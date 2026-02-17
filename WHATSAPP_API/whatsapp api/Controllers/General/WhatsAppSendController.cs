using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Whatsapp_API.Business.General;
using Whatsapp_API.Business.Integrations;
using Whatsapp_API.Helpers;
using Whatsapp_API.Models.Entities.Messaging;
using Whatsapp_API.Models.Helpers;
using Whatsapp_API.Models.Request.General;

namespace Whatsapp_API.Controllers.General
{
    // envíos por whatsapp desde la api (texto, plantilla, audio)

    [Produces("application/json")]
    [Route("api/integraciones/whatsapp/send")]
    [ApiController]
    public class WhatsAppSendController : ControllerBase
    {
        private readonly WhatsappSender _sender;
        private readonly ContactBus _contactoBus;
        private readonly ConversationBus _conversacionBus;
        private readonly MessageBus _mensajeBus;
        private readonly EmailHelper _correo;

        public WhatsAppSendController(
            WhatsappSender sender,
            ContactBus contactoBus,
            ConversationBus conversacionBus,
            MessageBus mensajeBus,
            EmailHelper correo)
        {
            _sender = sender;
            _contactoBus = contactoBus;
            _conversacionBus = conversacionBus;
            _mensajeBus = mensajeBus;
            _correo = correo;
        }

        // =========================
        // Enviar TEXTO (agente)
        // =========================
        [HttpPost("text")]
        [Authorize]
        public async Task<ActionResult> SendText([FromBody] SendTextRequest req)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                // contacto
                var (okContact, contacto, errContact) = ResolveOrCreateContact(req.Contact_Id, req.To_Phone);
                if (!okContact || contacto == null)
                    return NotFound(new { mensaje = errContact ?? "Contacto no encontrado" });

                // conversación
                var (okConv, convId, justCreated, errConv) =
                    ResolveOrCreateConversation(req.Conversation_Id, contacto.Id, req.Create_If_Not_Exists);
                if (!okConv)
                    return BadRequest(new { mensaje = errConv ?? "No se pudo abrir conversación" });

                // Envío a WA
                var send = await _sender.SendTextAsync(contacto.PhoneNumber!, req.Text);
                if (!send.Exitoso) return StatusCode(send.StatusCode, send);

                if (req.Log)
                {
                    // Leer estado actual ANTES de registrar el mensaje
                    var meta = _conversacionBus.Find(convId);
                    if (!meta.Exitoso || meta.Data == null)
                        return NotFound(new { mensaje = "Conversación no encontrada tras el envío." });

                    var wasClosed = string.Equals(meta.Data.Status, "closed", StringComparison.OrdinalIgnoreCase);

                    // Registrar mensaje
                    var m = new Message
                    {
                        ConversationId = convId,
                        ContactId = contacto.Id,
                        Sender = "agent",
                        Messages = req.Text,
                        Type = "text",
                        SentAt = DateTime.UtcNow
                    };
                    _mensajeBus.Create(m);

                    // Forzar estado final según wasClosed
                    var conv = meta.Data;
                    conv.LastActivityAt = DateTime.UtcNow;

                    if (wasClosed)
                    {
                        conv.Status = "closed";
                        conv.EndedAt ??= DateTime.UtcNow;
                    }
                    else
                    {
                        conv.Status = "open";
                    }

                    _conversacionBus.Update(conv);
                }

                return Ok(new { exitoso = true, conversacion_id = convId, just_created = justCreated });
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, req);
                return StatusCode(500, new { mensaje = ex.Message });
            }
        }

        // =========================
        // Enviar PLANTILLA (agente)
        // =========================
        [HttpPost("template")]
        [Authorize]
        public async Task<ActionResult> SendTemplate([FromBody] SendTemplateRequest req)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                // contacto
                var (okContact, contacto, errContact) = ResolveOrCreateContact(req.Contact_Id, req.To_Phone);
                if (!okContact || contacto == null)
                    return NotFound(new { mensaje = errContact ?? "Contacto no encontrado" });

                // conversación
                var (okConv, convId, justCreated, errConv) =
                    ResolveOrCreateConversation(req.Conversation_Id, contacto.Id, req.Create_If_Not_Exists);
                if (!okConv)
                    return BadRequest(new { mensaje = errConv ?? "No se pudo abrir conversación" });

                // Header de ubicación (opcional/obligatorio según plantilla)
                TemplateHeaderLocation? headerLoc = null;
                if (req.Header_Location_Latitude.HasValue && req.Header_Location_Longitude.HasValue)
                {
                    headerLoc = new TemplateHeaderLocation(
                        req.Header_Location_Latitude.Value,
                        req.Header_Location_Longitude.Value,
                        req.Header_Location_Name,
                        req.Header_Location_Address
                    );
                }

                bool plantillaRequiereLocation =
                    req.Template_Name.Contains("ubicacion", StringComparison.OrdinalIgnoreCase) ||
                    req.Template_Name.Contains("location", StringComparison.OrdinalIgnoreCase);

                if (plantillaRequiereLocation && headerLoc == null)
                {
                    return BadRequest(new { mensaje = "Suministre las coordenadas (lat/long) para esta plantilla." });
                }

                // Envío a WA
                var send = await _sender.SendTemplateAsync(
                    contacto.PhoneNumber!,
                    req.Template_Name,
                    req.Language,
                    req.Body_Vars ?? new(),
                    headerLoc
                );
                if (!send.Exitoso) return StatusCode(send.StatusCode, send);

                if (req.Log)
                {
                    // Leer estado actual ANTES de registrar el mensaje
                    var meta = _conversacionBus.Find(convId);
                    if (!meta.Exitoso || meta.Data == null)
                        return NotFound(new { mensaje = "Conversación no encontrada tras el envío." });

                    var wasClosed = string.Equals(meta.Data.Status, "closed", StringComparison.OrdinalIgnoreCase);

                    // Registrar mensaje
                    var m = new Message
                    {
                        ConversationId = convId,
                        ContactId = contacto.Id,
                        Sender = "agent",
                        Messages = $"(template) {req.Template_Name} [{req.Language}]",
                        Type = "template",
                        SentAt = DateTime.UtcNow
                    };
                    _mensajeBus.Create(m);

                    // Forzar estado final según wasClosed
                    var conv = meta.Data;
                    conv.LastActivityAt = DateTime.UtcNow;

                    if (wasClosed)
                    {
                        conv.Status = "closed";
                        conv.EndedAt ??= DateTime.UtcNow;
                    }
                    else
                    {
                        conv.Status = "open";
                    }

                    _conversacionBus.Update(conv);
                }

                return Ok(new { exitoso = true, conversacion_id = convId, just_created = justCreated });
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, req);
                return StatusCode(500, new { mensaje = ex.Message });
            }
        }

        // =========================
        // Enviar AUDIO (agente) -> ENVÍA A WHATSAPP
        // =========================
        [HttpPost("audio")]
        [Authorize]
        public async Task<ActionResult> SendAudio(
            IFormFile file,
            [FromForm] int? Conversation_Id,
            [FromForm] int? Contact_Id,
            [FromForm] string? To_Phone,
            [FromForm] bool Log = true,
            [FromForm] bool Create_If_Not_Exists = false)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return BadRequest(new { mensaje = "Archivo de audio vacío." });

                // Resolver contacto (igual que texto/plantilla)
                var (okContact, contacto, errContact) = ResolveOrCreateContact(Contact_Id, To_Phone);
                if (!okContact || contacto == null)
                    return NotFound(new { mensaje = errContact ?? "Contacto no encontrado" });

                // Resolver conversación
                var (okConv, convId, justCreated, errConv) =
                    ResolveOrCreateConversation(Conversation_Id, contacto.Id, Create_If_Not_Exists);
                if (!okConv)
                    return BadRequest(new { mensaje = errConv ?? "No se pudo abrir conversación" });

                // Enviar audio a WhatsApp: subir media + enviar mensaje
                using (var stream = file.OpenReadStream())
                {
                    var send = await _sender.SendAudioAsync(
                        contacto.PhoneNumber!,
                        stream,
                        file.FileName,
                        file.ContentType ?? "audio/ogg"
                    );

                    if (!send.Exitoso)
                        return StatusCode(send.StatusCode, send);
                }

                if (Log)
                {
                    // Leer estado actual ANTES de registrar el mensaje
                    var meta = _conversacionBus.Find(convId);
                    if (!meta.Exitoso || meta.Data == null)
                        return NotFound(new { mensaje = "Conversación no encontrada tras el envío." });

                    var wasClosed = string.Equals(meta.Data.Status, "closed", StringComparison.OrdinalIgnoreCase);

                    // Registrar mensaje como "audio"
                    var m = new Message
                    {
                        ConversationId = convId,
                        ContactId = contacto.Id,
                        Sender = "agent",
                        Messages = "(audio)",
                        Type = "audio",
                        SentAt = DateTime.UtcNow
                    };
                    _mensajeBus.Create(m);

                    // Forzar estado final según wasClosed
                    var conv = meta.Data;
                    conv.LastActivityAt = DateTime.UtcNow;

                    if (wasClosed)
                    {
                        conv.Status = "closed";
                        conv.EndedAt ??= DateTime.UtcNow;
                    }
                    else
                    {
                        conv.Status = "open";
                    }

                    _conversacionBus.Update(conv);
                }

                return Ok(new
                {
                    exitoso = true,
                    conversacion_id = convId,
                    just_created = justCreated
                });
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, new { Conversation_Id, Contact_Id, To_Phone });
                return StatusCode(500, new { mensaje = ex.Message });
            }
        }

        // =========================
        // Helpers
        // =========================
        private (bool ok, Contact? c, string? err) ResolveOrCreateContact(int? contactId, string? toPhone)
        {
            if (contactId.HasValue && contactId.Value > 0)
            {
                var f = _contactoBus.Find(contactId.Value);
                if (f.Exitoso && f.Data != null) return (true, f.Data, null);
                return (false, null, "Contacto no existe.");
            }

            if (string.IsNullOrWhiteSpace(toPhone))
                return (false, null, "Debe enviar Contact_Id o To_Phone.");

            var byPhone = _contactoBus.FindByPhone(toPhone);
            if (byPhone.Exitoso && byPhone.Data != null) return (true, byPhone.Data, null);

            // Crear si no existe (nombre = teléfono)
            var nuevo = new Contact
            {
                Name = toPhone,
                PhoneNumber = toPhone,
                CreatedAt = DateTime.UtcNow,
                LastMessageAt = DateTime.UtcNow,
                Status = "active",
                WelcomeSent = false
            };
            var r = _contactoBus.Create(nuevo);
            if (!r.Exitoso) return (false, null, "No se pudo crear el contacto.");

            var again = _contactoBus.FindByPhone(toPhone);
            return (again.Exitoso, again.Data, again.Exitoso ? null : "No se pudo obtener contacto");
        }

        private (bool ok, int convId, bool justCreated, string? err) ResolveOrCreateConversation(int? convId, int contactId, bool createIfMissing)
        {
            if (convId.HasValue && convId.Value > 0)
            {
                var f = _conversacionBus.Find(convId.Value);
                if (f.Exitoso && f.Data != null) return (true, convId.Value, false, null);
                return (false, 0, false, "Conversación no existe.");
            }

            // buscar abierta “fresca”
            var open = _conversacionBus.FindOpenByContactStrict(contactId, freshOnly: true);
            if (open.Exitoso && open.Data != null) return (true, open.Data.Id, false, null);

            if (!createIfMissing)
                return (false, 0, false, "No hay conversación abierta y Create_If_Not_Exists = false.");

            // crear
            var conv = new Conversation
            {
                ContactId = contactId,
                StartedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow,
                EndedAt = null,
                Status = "open",
                GreetingSent = false,
                TotalMessages = 0,
                AiMessages = 0,
                FirstResponseTime = null,
                Rating = null
            };
            var r = _conversacionBus.Create(conv);
            if (!r.Exitoso || r.Data == null) return (false, 0, false, "No se pudo crear conversación.");
            return (true, r.Data.Id, true, null);
        }
    }
}
