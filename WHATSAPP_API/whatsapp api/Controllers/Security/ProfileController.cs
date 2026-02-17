using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Whatsapp_API.Business.Security;
using Whatsapp_API.Helpers;
using Whatsapp_API.Models.Entities.Security;
using Whatsapp_API.Models.Helpers;
using Whatsapp_API.Models.Request.Security;

namespace Whatsapp_API.Controllers.Security
{
    // perfiles: listar, buscar, crear/editar, borrar y cargar por defecto

    [Produces("application/json")]
    [Route("api/seguridad/[controller]")]
    [ApiController]
    public class ProfileController : ControllerBase
    {
        private readonly ProfileBus _bus;
        private readonly EmailHelper _correo;

        public ProfileController(ProfileBus bus, EmailHelper correo)
        {
            _bus = bus;
            _correo = correo;
        }


        // lista todos
        [HttpGet]
        public ActionResult Get()
        {
            try { return _bus.List().StatusCodeDescriptivo(); }
            catch (Exception ex) { _correo.EnviarCorreoError(ex); return StatusCode(500, ex.Message); }
        }

        // trae uno por id
        [HttpGet("{id:int}")]
        public ActionResult Get(int id)
        {
            try { return _bus.Find(id).StatusCodeDescriptivo(); }
            catch (Exception ex) { _correo.EnviarCorreoError(ex, new { id }); return StatusCode(500, ex.Message); }
        }

        // crear o actualizar según venga el id

        [HttpPost("upsert")]
        [Consumes("application/json")]
        [Produces("application/json")]
        [ProducesResponseType(typeof(DescriptiveBoolean), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(DescriptiveBoolean), StatusCodes.Status201Created)]
        public ActionResult Upsert([FromBody] ProfileUpsertRequest req)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                if (req.Id == 0)
                {
                    if (string.IsNullOrWhiteSpace(req.Name))
                        return BadRequest(new { mensaje = "Nombre requerido" });

                    if (_bus.ExistsNombre(req.Name!))
                        return Conflict(new { mensaje = "Ya existe un perfil con ese nombre" });

                    var r = _bus.Create(new Profile { Name = req.Name });
                    return r.StatusCodeDescriptivo();
                }
                else
                {
                    var p = new Profile { Id = req.Id, Name = req.Name };
                    var r = _bus.Update(p);
                    return r.StatusCodeDescriptivo();
                }
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, req);
                return new DescriptiveBoolean
                {
                    Exitoso = false,
                    Mensaje = "Error al crear/actualizar",
                    StatusCode = 500
                }.StatusCodeDescriptivo();
            }
        }

        // elimina por id

        [HttpGet("Delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            try { return _bus.Delete(id).StatusCodeDescriptivo(); }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, new { id });
                return new DescriptiveBoolean
                {
                    Exitoso = false,
                    Mensaje = "Error al eliminar",
                    StatusCode = 500
                }.StatusCodeDescriptivo();
            }
        }

        // crea los perfiles base si no existen

        [HttpPost("seed-defaults")]
        public ActionResult SeedDefaults()
        {
            try { return _bus.EnsureDefaults().StatusCodeDescriptivo(); }
            catch (Exception ex) { _correo.EnviarCorreoError(ex); return StatusCode(500, ex.Message); }
        }
    }
}
