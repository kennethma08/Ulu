using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Whatsapp_API.Data;
using Whatsapp_API.Models.Entities.Security;
using Whatsapp_API.Models.Request.Authentication;
using Whatsapp_API.Helpers;

namespace Whatsapp_API.Controllers.VAMMP
{
    [Produces("application/json")]
    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly MyDbContext _db;
        private readonly IConfiguration _cfg;

        public AuthController(MyDbContext db, IConfiguration cfg)
        {
            _db = db;
            _cfg = cfg;
        }

        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var input = (req.UserName ?? "").Trim().ToLower();

            // ⛔ ANTES: AsNoTracking() -> no permite actualizar el usuario
            // ✅ AHORA: sin AsNoTracking para poder modificar y hacer SaveChanges
            var user = await _db.Users
                .FirstOrDefaultAsync(u =>
                    (u.Email != null && u.Email.ToLower() == input) ||
                    (u.Phone != null && u.Phone.ToLower() == input));

            // Toda la lógica de comparación está en PasswordHelper
            if (user == null || !PasswordHelper.VerifyPassword(req.Password, user.Pass))
            {
                return Unauthorized(new { message = "Usuario o contraseña inválidos." });
            }

            // ====== ACTUALIZAR CAMPOS DE SESIÓN / ONLINE ======
            // Guardar directamente en hora de Costa Rica (UTC-6)
            var now = DateTime.UtcNow.AddHours(-6); // Costa Rica

            // Si nunca ha hecho login, setear LastLogin también
            if (!user.LastLogin.HasValue)
                user.LastLogin = now;
            else
                user.LastLogin = now;   // si quieres, siempre puedes pisarlo

            user.LastActivity = now;
            user.IsOnline = true;

            await _db.SaveChangesAsync();
            // ================================================

            var roleName = await _db.Profiles
                .AsNoTracking()
                .Where(p => p.Id == user.IdProfile)
                .Select(p => p.Name!)
                .FirstOrDefaultAsync() ?? "Usuario";

            var empresaId = user.CompanyId ?? 0;

            var token = GenerateJwtToken(user, roleName, empresaId);
            return Ok(new
            {
                token,
                user = new
                {
                    id = user.Id,
                    nombre = user.Name,
                    correo = user.Email,
                    role = roleName,
                    empresa_id = empresaId
                }
            });
        }

        [Authorize]
        [HttpGet("me")]
        public IActionResult Me()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var nombre = User.FindFirstValue(ClaimTypes.Name) ?? "";
            var correo = User.FindFirstValue(ClaimTypes.Email) ?? "";
            var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
            var empresa = User.FindFirst("empresa_id")?.Value
                       ?? User.FindFirst("company_id")?.Value;

            return Ok(new { id = userId, nombre, correo, role, empresa_id = empresa });
        }

        private string GenerateJwtToken(User user, string role, int empresaId)
        {
            var jwtSection = _cfg.GetSection("Jwt");

            var key = jwtSection["Key"]!;
            var issuer = jwtSection["Issuer"]!;
            var audience = jwtSection["Audience"]!;

            var creds = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(ClaimTypes.NameIdentifier,    user.Id.ToString()),
                new Claim(ClaimTypes.Name,  user.Name ?? ""),
                new Claim(ClaimTypes.Email, user.Email ?? ""),
                new Claim(ClaimTypes.Role,  role),
                new Claim("empresa_id",     empresaId.ToString())
            };

            var expiresMinutes = int.TryParse(jwtSection["ExpiresMinutes"], out var m) ? m : 120;

            // El token sigue con expiración en UTC (esto sí debe quedar en UTC)
            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(expiresMinutes),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
