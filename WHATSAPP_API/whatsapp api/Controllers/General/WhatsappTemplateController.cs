using Microsoft.AspNetCore.Mvc;
using Whatsapp_API.Helpers;
using Whatsapp_API.Models.Helpers;
using Whatsapp_API.Business.Integrations;
using Whatsapp_API.Models.Entities.System;

namespace Whatsapp_API.Controllers.General
{
    // plantillas de whatsapp ver, crear o editar, borrar y probar envío

    [Produces("application/json")]
    [Route("api/integraciones/[controller]")]
    [ApiController]
    public class WhatsappTemplateController : ControllerBase
    {
        private readonly WhatsappTemplateBus _bus;
        private readonly EmailHelper _correo;
        private readonly WhatsappSender _sender;

        public WhatsappTemplateController(WhatsappTemplateBus bus, EmailHelper correo, WhatsappSender sender)
        { _bus = bus; _correo = correo; _sender = sender; }

        // lista todas

        [HttpGet] public ActionResult Get() { try { return _bus.List().StatusCodeDescriptivo(); } catch (Exception ex) { _correo.EnviarCorreoError(ex); return StatusCode(500, ex.Message); } }

        // trae una por id
        [HttpGet("{id:int}")] public ActionResult Get(int id) { try { return _bus.Find(id).StatusCodeDescriptivo(); } catch (Exception ex) { _correo.EnviarCorreoError(ex, new { id }); return StatusCode(500, ex.Message); } }


        // crea o actualiza una plantilla
        [HttpPost("upsert")]
        public ActionResult Upsert([FromBody] WhatsappTemplate req)
        { try { return _bus.Upsert(req).StatusCodeDescriptivo(); } catch (Exception ex) { _correo.EnviarCorreoError(ex, req); return StatusCode(500, ex.Message); } }


        // elimina por id
        [HttpDelete("{id:int}")]
        public ActionResult Delete(int id)
        { try { return _bus.Delete(id).StatusCodeDescriptivo(); } catch (Exception ex) { _correo.EnviarCorreoError(ex, new { id }); return StatusCode(500, ex.Message); } }


        // prueba rápida de envío por nombre e idioma

        [HttpPost("send")]
        public async Task<ActionResult> Send([FromBody] dynamic body)
        {
            try
            {
                string to = body.to;// a quién se envía
                string name = body.name; // nombre de la plantilla
                string lang = body.lang ?? "es"; // idioma de la plantilla por defecto es español
                List<string> vars = ((IEnumerable<object>)body.vars ?? Array.Empty<object>()).Select(v => v?.ToString() ?? "").ToList();

                var r = await _sender.SendTemplateAsync(to, name, lang, vars);
                return r.StatusCodeDescriptivo();
            }
            catch (Exception ex) { _correo.EnviarCorreoError(ex, body); return StatusCode(500, ex.Message); }
        }
    }
}
