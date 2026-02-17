using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Whatsapp_API.Business.General;
using Whatsapp_API.Helpers;
using Whatsapp_API.Models.Entities.Messaging;
using Whatsapp_API.Models.Helpers;
using Whatsapp_API.Models.Request.General;

namespace Whatsapp_API.Controllers.General
{
    [Produces("application/json")]
    [Route("api/general/[controller]")]
    [ApiController]
    public class ConversationController : ControllerBase
    {
        private readonly ConversationBus _bus;
        private readonly EmailHelper _correo;

        public ConversationController(ConversationBus bus, EmailHelper correo)
        { _bus = bus; _correo = correo; }

        [HttpGet]
        public ActionResult Get()
        {
            try { return _bus.List().StatusCodeDescriptivo(); }
            catch (Exception ex) { _correo.EnviarCorreoError(ex); return StatusCode(500, ex.Message); }
        }

        [HttpGet("{id:int}")]
        public ActionResult Get(int id)
        {
            try { return _bus.Find(id).StatusCodeDescriptivo(); }
            catch (Exception ex) { _correo.EnviarCorreoError(ex, new { id }); return StatusCode(500, ex.Message); }
        }

        [HttpPost("upsert")]
        public ActionResult Upsert([FromBody] ConversationUpsertRequest req)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                if (req.Id == 0)
                {
                    var status = string.IsNullOrWhiteSpace(req.Status) ? "open" : req.Status.Trim();
                    var c = new Conversation
                    {
                        ContactId = req.Contact_Id,
                        StartedAt = req.Started_At ?? DateTime.UtcNow,
                        LastActivityAt = req.Last_Activity_At,
                        EndedAt = req.Ended_At,
                        Status = status,
                        GreetingSent = req.Greeting_Sent ?? false,
                        TotalMessages = req.Total_Messages ?? 0,
                        AiMessages = req.Ai_Messages ?? 0,
                        FirstResponseTime = req.First_Response_Time,
                        Rating = req.Rating,
                        ClosedByUserId = null,

                        // nuevo hold: por defecto no
                        IsOnHold = false,
                        OnHoldReason = null,
                        OnHoldAt = null,
                        OnHoldByUserId = null
                    };
                    var r = _bus.Create(c);
                    return r.StatusCodeDescriptivo();
                }

                var encontrado = _bus.Find(req.Id);
                if (!encontrado.Exitoso || encontrado.Data == null)
                    return NotFound(new { mensaje = "Conversación no encontrada" });

                var conv = encontrado.Data;

                if (!string.IsNullOrWhiteSpace(req.Status) &&
                    (conv.Status ?? "").Trim().Equals("closed", StringComparison.OrdinalIgnoreCase) &&
                    req.Status.Trim().Equals("open", StringComparison.OrdinalIgnoreCase))
                    return StatusCode(StatusCodes.Status409Conflict,
                        new { exitoso = false, mensaje = "No se permite reabrir una conversación cerrada.", statusCode = 409 });

                conv.ContactId = req.Contact_Id != 0 ? req.Contact_Id : conv.ContactId;
                conv.StartedAt = req.Started_At ?? conv.StartedAt;
                conv.LastActivityAt = req.Last_Activity_At ?? conv.LastActivityAt;
                conv.EndedAt = req.Ended_At ?? conv.EndedAt;

                var newStatus = string.IsNullOrWhiteSpace(req.Status) ? conv.Status : req.Status.Trim();
                var wasClosed = (conv.Status ?? "").Equals("closed", StringComparison.OrdinalIgnoreCase);
                var willBeClosed = (newStatus ?? "").Equals("closed", StringComparison.OrdinalIgnoreCase);

                if (!wasClosed && willBeClosed)
                {
                    conv.EndedAt ??= DateTime.UtcNow;
                    var uid = GetCurrentUserId();
                    if (uid.HasValue) conv.ClosedByUserId = uid.Value;

                    // si cierra, limpiar hold
                    conv.IsOnHold = false;
                    conv.OnHoldReason = null;
                    conv.OnHoldAt = null;
                    conv.OnHoldByUserId = null;
                }

                conv.Status = newStatus;
                conv.GreetingSent = req.Greeting_Sent ?? conv.GreetingSent;
                conv.TotalMessages = req.Total_Messages ?? conv.TotalMessages;
                conv.AiMessages = req.Ai_Messages ?? conv.AiMessages;
                conv.FirstResponseTime = req.First_Response_Time ?? conv.FirstResponseTime;
                conv.Rating = req.Rating ?? conv.Rating;

                var rUpd = _bus.Update(conv);
                return rUpd.StatusCodeDescriptivo();
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, req);
                return new DescriptiveBoolean { Exitoso = false, Mensaje = "Error al crear/actualizar", StatusCode = 500 }
                    .StatusCodeDescriptivo();
            }
        }

        [HttpPost("open-or-create")]
        [AllowAnonymous]
        public ActionResult OpenOrCreate([FromBody] ConversationOpenOrCreateRequest req)
        {
            try
            {
                if (req.Contact_Id <= 0) return BadRequest(new { mensaje = "Contact_Id requerido" });

                var open = _bus.FindOpenByContactStrict(req.Contact_Id, freshOnly: req.Fresh_Only ?? true);
                if (open.Exitoso && open.Data != null)
                    return Ok(new
                    {
                        data = new
                        {
                            id = open.Data.Id,
                            created = false,
                            status = open.Data.Status,
                            started_at = open.Data.StartedAt,
                            last_activity_at = open.Data.LastActivityAt,
                            is_on_hold = open.Data.IsOnHold,
                            on_hold_reason = open.Data.OnHoldReason,
                            on_hold_at = open.Data.OnHoldAt
                        }
                    });

                var now = req.Started_At ?? DateTime.UtcNow;
                var c = new Conversation
                {
                    ContactId = req.Contact_Id,
                    StartedAt = now,
                    LastActivityAt = now,
                    EndedAt = null,
                    Status = "open",
                    GreetingSent = false,
                    TotalMessages = 0,
                    AiMessages = 0,
                    FirstResponseTime = null,
                    Rating = null,

                    IsOnHold = false,
                    OnHoldReason = null,
                    OnHoldAt = null,
                    OnHoldByUserId = null
                };

                var r = _bus.Create(c);
                if (!r.Exitoso || r.Data == null) return r.StatusCodeDescriptivo();

                return Ok(new
                {
                    data = new
                    {
                        id = r.Data.Id,
                        created = true,
                        status = r.Data.Status,
                        started_at = r.Data.StartedAt,
                        last_activity_at = r.Data.LastActivityAt,
                        is_on_hold = r.Data.IsOnHold
                    }
                });
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, req);
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("{id:int}/touch")]
        [AllowAnonymous]
        public ActionResult Touch(int id, [FromBody] ConversationTouchRequest req)
        {
            try
            {
                var found = _bus.Find(id);
                if (!found.Exitoso || found.Data == null) return NotFound(new { mensaje = "Conversación no encontrada" });

                var c = found.Data;
                c.LastActivityAt = req.Last_Activity_At ?? DateTime.UtcNow;
                c.Status = string.IsNullOrWhiteSpace(req.Status) ? "open" : req.Status!.Trim();

                var r = _bus.Update(c);
                return r.StatusCodeDescriptivo();
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, new { id, req });
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("{id:int}/close")]
        public ActionResult Close(int id, [FromBody] ConversationCloseRequest req)
        {
            try
            {
                var found = _bus.Find(id);
                if (!found.Exitoso || found.Data == null) return NotFound(new { mensaje = "Conversación no encontrada" });

                var c = found.Data;
                if (!string.Equals(c.Status, "closed", StringComparison.OrdinalIgnoreCase))
                {
                    c.Status = "closed";
                    c.EndedAt = req.Ended_At ?? DateTime.UtcNow;
                    var uid = GetCurrentUserId();
                    if (uid.HasValue) c.ClosedByUserId = uid.Value;

                    // cerrar limpia hold
                    c.IsOnHold = false;
                    c.OnHoldReason = null;
                    c.OnHoldAt = null;
                    c.OnHoldByUserId = null;
                }

                var r = _bus.Update(c);
                return r.StatusCodeDescriptivo();
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, new { id, req });
                return StatusCode(500, ex.Message);
            }
        }

        // NUEVO: poner en espera
        [HttpPost("{id:int}/hold")]
        public ActionResult Hold(int id, [FromBody] ConversationHoldRequest req)
        {
            try
            {
                var found = _bus.Find(id);
                if (!found.Exitoso || found.Data == null)
                    return NotFound(new { mensaje = "Conversación no encontrada" });

                var c = found.Data;

                if (string.Equals(c.Status, "closed", StringComparison.OrdinalIgnoreCase))
                    return StatusCode(StatusCodes.Status409Conflict,
                        new { exitoso = false, mensaje = "No se permite poner en espera una conversación cerrada.", statusCode = 409 });

                var reason = (req?.Reason ?? "").Trim();
                if (reason.Length > 500) reason = reason.Substring(0, 500);

                c.IsOnHold = true;
                c.OnHoldReason = string.IsNullOrWhiteSpace(reason) ? null : reason;
                c.OnHoldAt = DateTime.UtcNow;

                var uid = GetCurrentUserId();
                c.OnHoldByUserId = uid;

                var r = _bus.Update(c);
                if (!r.Exitoso) return r.StatusCodeDescriptivo();

                return Ok(new
                {
                    exitoso = true,
                    data = new
                    {
                        id = c.Id,
                        status = c.Status,
                        is_on_hold = c.IsOnHold,
                        on_hold_reason = c.OnHoldReason,
                        on_hold_at = c.OnHoldAt,
                        on_hold_by_user_id = c.OnHoldByUserId
                    }
                });
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, new { id, req });
                return StatusCode(500, ex.Message);
            }
        }

        // NUEVO: quitar de espera
        [HttpPost("{id:int}/resume")]
        public ActionResult Resume(int id)
        {
            try
            {
                var found = _bus.Find(id);
                if (!found.Exitoso || found.Data == null)
                    return NotFound(new { mensaje = "Conversación no encontrada" });

                var c = found.Data;

                if (string.Equals(c.Status, "closed", StringComparison.OrdinalIgnoreCase))
                    return StatusCode(StatusCodes.Status409Conflict,
                        new { exitoso = false, mensaje = "No se permite reanudar una conversación cerrada.", statusCode = 409 });

                c.IsOnHold = false;
                c.OnHoldReason = null;
                c.OnHoldAt = null;
                c.OnHoldByUserId = null;

                var r = _bus.Update(c);
                if (!r.Exitoso) return r.StatusCodeDescriptivo();

                return Ok(new
                {
                    exitoso = true,
                    data = new
                    {
                        id = c.Id,
                        status = c.Status,
                        is_on_hold = c.IsOnHold
                    }
                });
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, new { id });
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("Delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            try { return _bus.Delete(id).StatusCodeDescriptivo(); }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, new { id });
                return new DescriptiveBoolean { Exitoso = false, Mensaje = "Error al eliminar", StatusCode = 500 }
                    .StatusCodeDescriptivo();
            }
        }

        [HttpGet("by-contact/{contactId:int}/open")]
        [AllowAnonymous]
        public ActionResult GetOpenByContact(int contactId, [FromQuery] bool freshOnly = false)
        {
            try
            {
                var r = _bus.FindOpenByContactStrict(contactId, freshOnly);
                if (!r.Exitoso || r.Data == null) return NotFound(new { data = (object?)null });

                var c = r.Data;
                return Ok(new
                {
                    data = new
                    {
                        id = c.Id,
                        contact_id = c.ContactId,
                        status = c.Status,
                        started_at = c.StartedAt,
                        last_activity_at = c.LastActivityAt,
                        is_on_hold = c.IsOnHold,
                        on_hold_reason = c.OnHoldReason,
                        on_hold_at = c.OnHoldAt
                    }
                });
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, new { contactId, freshOnly });
                return StatusCode(500, ex.Message);
            }
        }

        private int? GetCurrentUserId()
        {
            var s = User.FindFirst("id")?.Value
                 ?? User.FindFirst("user_id")?.Value
                 ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return int.TryParse(s, out var id) ? id : (int?)null;
        }
    }
}
