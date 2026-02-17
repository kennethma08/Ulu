using Microsoft.AspNetCore.Mvc;
using Whatsapp_API.Business.Integrations;
using Whatsapp_API.Helpers;
using Whatsapp_API.Models.Helpers;
using Whatsapp_API.Models.Request.General;

namespace Whatsapp_API.Controllers.General
{

    // integraciones listar activa y crear/editar

    [Produces("application/json")]
    [Route("api/general/[controller]")]
    [ApiController]
    public class IntegrationController : ControllerBase
    {
        private readonly IntegrationBus _bus;
        private readonly EmailHelper _correo;

        public IntegrationController(IntegrationBus bus, EmailHelper correo)
        {
            _bus = bus;
            _correo = correo;
        }

        // devuelve la integración activa

        [HttpGet]
        public ActionResult Get()
        {
            try
            {
                return _bus.GetActive().StatusCodeDescriptivo();
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex);
                return StatusCode(500, ex.Message);
            }
        }

        // crea/actualiza datos de la integración
        [HttpPost("upsert")]
        public ActionResult Upsert([FromBody] IntegrationUpsertRequest req)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);
                return _bus.Upsert(req).StatusCodeDescriptivo();
            }
            catch (Exception ex)
            {
                _correo.EnviarCorreoError(ex, req);
                return StatusCode(500, ex.Message);
            }
        }
    }
}
