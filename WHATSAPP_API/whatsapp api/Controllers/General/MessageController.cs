using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Whatsapp_API.Business.General;
using Whatsapp_API.Helpers;
using Whatsapp_API.Models.Helpers;
using Whatsapp_API.Models.Entities.Messaging;
using Whatsapp_API.Models.Request.General;

namespace Whatsapp_API.Controllers.General
{
    [Produces("application/json")]
    [Route("api/general/[controller]")]
    [ApiController]
    public class MessageController : ControllerBase
    {
        private readonly MessageBus _bus;
        private readonly EmailHelper _correo;

        public MessageController(MessageBus bus, EmailHelper correo)
        {
            _bus = bus;
            _correo = correo;
        }

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
        [Consumes("application/json")]
        [Produces("application/json")]
        [ProducesResponseType(typeof(DescriptiveBoolean), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(DescriptiveBoolean), StatusCodes.Status201Created)]
        public ActionResult Upsert([FromBody] MessageUpsertRequest req)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                if (req.Id == 0)
                {
                    var m = new Message
                    {
                        ConversationId = req.Conversation_Id,
                        ContactId = req.Contact_Id,
                        // Agent_Id eliminado
                        Sender = req.Sender,
                        Messages = req.Message,
                        Type = req.Type,
                        SentAt = req.Sent_At ?? DateTime.UtcNow,
                        Latitude = req.Latitude,
                        Longitude = req.Longitude,
                        LocationName = req.Location_Name
                    };

                    var r = _bus.Create(m);
                    return r.StatusCodeDescriptivo();
                }
                else
                {
                    var encontrado = _bus.Find(req.Id);
                    if (!encontrado.Exitoso || encontrado.Data == null)
                        return NotFound(new { mensaje = "Mensaje no encontrado" });

                    var m = encontrado.Data;

                    m.ConversationId = req.Conversation_Id != 0 ? req.Conversation_Id : m.ConversationId;
                    m.ContactId = req.Contact_Id != 0 ? req.Contact_Id : m.ContactId;
                    // m.AgentId = req.Agent_Id ?? m.AgentId; // eliminado
                    m.Sender = req.Sender ?? m.Sender;
                    m.Messages = req.Message ?? m.Messages;
                    m.Type = req.Type ?? m.Type;
                    m.SentAt = req.Sent_At ?? m.SentAt;
                    m.Latitude = req.Latitude ?? m.Latitude;
                    m.Longitude = req.Longitude ?? m.Longitude;
                    m.LocationName = req.Location_Name ?? m.LocationName;

                    var r = _bus.Update(m);
                    return r.StatusCodeDescriptivo();
                }
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, req);
                return new DescriptiveBoolean { Exitoso = false, Mensaje = "Error al crear/actualizar", StatusCode = 500 }
                    .StatusCodeDescriptivo();
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
    }
}
