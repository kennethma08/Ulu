using Microsoft.AspNetCore.Mvc;
using Whatsapp_API.Business.VAMMP;

namespace Whatsapp_API.Controllers.VAMMP
{
    // consulta de usuario en vammp por correo

    [ApiController]
    [Route("api/vammp/usuario")]
    [Produces("application/json")]
    public class UserVAMMPController : ControllerBase
    {
        private readonly UserVAMMPBus _bus;
        public UserVAMMPController(UserVAMMPBus bus) => _bus = bus;

        [HttpGet("por-correo")]
        public async Task<ActionResult> PorCorreo([FromQuery] string token, [FromQuery] string correo)
        {
            var resp = await _bus.ConsultaUsuarioVammp(token, correo);
            return new ObjectResult(resp) { StatusCode = resp.StatusCode };
        }
    }
}
