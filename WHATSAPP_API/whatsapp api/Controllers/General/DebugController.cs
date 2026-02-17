using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Whatsapp_API.Infrastructure.MultiTenancy;

namespace Whatsapp_API.Controllers.General
{
    [ApiController]
    [Route("api/debug")]
    public class DebugController : ControllerBase
    {
        private readonly IHttpContextAccessor _http;
        private readonly TenantContext _tenant;

        public DebugController(IHttpContextAccessor http, TenantContext tenant)
        {
            _http = http;
            _tenant = tenant;
        }

        // No requiere autenticación: nos sirve para ver qué llegó realmente al API vía ngrok
        [AllowAnonymous]
        [HttpGet("echo")]
        public IActionResult Echo()
        {
            var ctx = _http.HttpContext!;
            var req = ctx.Request;

            var auth = req.Headers["Authorization"].ToString();
            var hasBearer = !string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
            var tokenPreview = hasBearer ? auth.Length > 30 ? auth.Substring(0, 30) + "..." : auth : "(none)";

            var empHeader = req.Headers["X-Empresa-Id"].FirstOrDefault()
                         ?? req.Headers["X-Empresa"].FirstOrDefault()
                         ?? "(none)";

            var claims = User?.Claims?.Select(c => new { c.Type, c.Value }).ToList() ?? new();

            return Ok(new
            {
                method = req.Method,
                path = req.Path.ToString(),
                hasBearer,
                tokenPreview, // truncado, no exponemos todo el token
                empHeader,
                tenantEmpresaId = _tenant?.CompanyId,
                claimsCount = claims.Count,
                claims
            });
        }
    }
}
