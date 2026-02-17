using Microsoft.AspNetCore.Mvc;
using Whatsapp_API.Business.General;
using Whatsapp_API.Helpers;
using Whatsapp_API.Models.Entities.Messaging;
using Whatsapp_API.Models.Helpers;
using Whatsapp_API.Models.Request.General;

namespace Whatsapp_API.Controllers.General
{

    // contactos listar, ver uno, crear/editar, marcar bienvenida y borrar

    [Produces("application/json")]
    [Route("api/general/[controller]")]
    [ApiController]
    public class ContactController : ControllerBase
    {
        private readonly ContactBus _bus;
        private readonly EmailHelper _correo;

        public ContactController(ContactBus bus, EmailHelper correo)
        { _bus = bus; _correo = correo; }

        //todos

        [HttpGet]
        public ActionResult Get()
        {
            try { return _bus.List().StatusCodeDescriptivo(); }
            catch (Exception ex) { _correo.EnviarCorreoError(ex); return StatusCode(500, ex.Message); }
        }

        //busca por id

        [HttpGet("{id:int}")]
        public ActionResult Get(int id)
        {
            try { return _bus.Find(id).StatusCodeDescriptivo(); }
            catch (Exception ex) { _correo.EnviarCorreoError(ex, new { id }); return StatusCode(500, ex.Message); }
        }

        //crear o actualizar

        [HttpPost("upsert")]
        public ActionResult Upsert([FromBody] ContactUpsertRequest req)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                if (req.Id == 0 && !string.IsNullOrWhiteSpace(req.Phone_Number))
                {
                    var porTelefono = _bus.FindByPhone(req.Phone_Number);
                    if (porTelefono.Exitoso && porTelefono.Data != null)
                    {
                        var c = porTelefono.Data;
                        c.Name = req.Name ?? c.Name;
                        c.PhoneNumber = req.Phone_Number ?? c.PhoneNumber;
                        c.Country = req.Country ?? c.Country;
                        c.CreatedAt = req.Created_At ?? c.CreatedAt;
                        c.LastMessageAt = req.Last_Message_At ?? c.LastMessageAt;
                        c.Status = req.Status ?? c.Status;
                        c.WelcomeSent = req.Welcome_Sent ?? c.WelcomeSent;
                        var r = _bus.Update(c);
                        return r.StatusCodeDescriptivo();
                    }
                }

                if (req.Id > 0)
                {
                    var encontrado = _bus.Find(req.Id);
                    if (!encontrado.Exitoso || encontrado.Data == null) return NotFound(new { mensaje = "Contacto no encontrado" });

                    var c = encontrado.Data;
                    c.Name = req.Name ?? c.Name;
                    c.PhoneNumber = req.Phone_Number ?? c.PhoneNumber;
                    c.Country = req.Country ?? c.Country;
                    c.CreatedAt = req.Created_At ?? c.CreatedAt;
                    c.LastMessageAt = req.Last_Message_At ?? c.LastMessageAt;
                    c.Status = req.Status ?? c.Status;
                    c.WelcomeSent = req.Welcome_Sent ?? c.WelcomeSent;
                    var r = _bus.Update(c);
                    return r.StatusCodeDescriptivo();
                }

                var nuevo = new Contact
                {
                    Name = req.Name,
                    PhoneNumber = req.Phone_Number,
                    Country = req.Country,
                    CreatedAt = req.Created_At ?? DateTime.UtcNow,
                    LastMessageAt = req.Last_Message_At,
                    Status = req.Status,
                    WelcomeSent = req.Welcome_Sent ?? false
                };
                var rCreate = _bus.Create(nuevo);
                return rCreate.StatusCodeDescriptivo();
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, req);
                return new DescriptiveBoolean { Exitoso = false, Mensaje = "Error al crear/actualizar", StatusCode = 500 }.StatusCodeDescriptivo();
            }
        }

        //*REVISAR SI FUNCIONA O ELIMINAR*

        [HttpPost("{id:int}/mark-welcome")]
        public ActionResult MarkWelcome(int id, [FromBody] ContactMarkWelcomeRequest req)
        {
            try
            {
                var found = _bus.Find(id);
                if (!found.Exitoso || found.Data == null) return NotFound(new { mensaje = "Contacto no encontrado" });

                var c = found.Data;
                c.WelcomeSent = true;
                c.LastMessageAt = req?.Last_Message_At ?? c.LastMessageAt ?? DateTime.UtcNow;

                var r = _bus.Update(c);
                return r.StatusCodeDescriptivo();
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, new { id, req });
                return StatusCode(500, ex.Message);
            }
        }

        //borrar por id

        [HttpGet("Delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            try { return _bus.Delete(id).StatusCodeDescriptivo(); }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, new { id });
                return new DescriptiveBoolean { Exitoso = false, Mensaje = "Error al eliminar", StatusCode = 500 }.StatusCodeDescriptivo();
            }
        }

        //buscar por teléfono

        [HttpGet("by-phone/{phone}")]
        public ActionResult GetByPhone(string phone)
        {
            try { return _bus.FindByPhone(phone).StatusCodeDescriptivo(); }
            catch (Exception ex) { _correo.EnviarCorreoError(ex, new { phone }); return StatusCode(500, ex.Message); }
        }


        // actualizar nombre
        public class UpdateContactoNombreRequest { public string Name { get; set; } = ""; }

        [HttpPatch("{id:int}/nombre")]
        public ActionResult UpdateNombre(int id, [FromBody] UpdateContactoNombreRequest req)
        {
            try
            {
                if (id <= 0 || string.IsNullOrWhiteSpace(req?.Name))
                    return BadRequest(new { mensaje = "Parámetros inválidos." });

                var found = _bus.Find(id);
                if (!found.Exitoso || found.Data == null)
                    return NotFound(new { mensaje = "Contacto no encontrado" });

                var c = found.Data;
                c.Name = req.Name.Trim();

                var r = _bus.Update(c);
                return r.StatusCodeDescriptivo();
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, new { id, req });
                return StatusCode(500, ex.Message);
            }
        }

    }
}
