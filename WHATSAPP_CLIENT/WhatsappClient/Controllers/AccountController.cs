using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using WhatsappClient.Services;
using WhatsappClient.Models;

namespace WhatsappClient.Controllers
{
    public class AccountController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IHttpContextAccessor _accessor;
        private readonly IConfiguration _cfg;

        public AccountController(IHttpClientFactory httpClientFactory, IHttpContextAccessor accessor, IConfiguration cfg)
        {
            _httpClientFactory = httpClientFactory;
            _accessor = accessor;
            _cfg = cfg;
        }

        // ====================== LOGIN ======================

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View(new LoginVm("", ""));
        }

        public record LoginVm(string Email, string Password);

        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> Login(LoginVm vm, string? returnUrl = null)
        {
            if (string.IsNullOrWhiteSpace(vm.Email) || string.IsNullOrWhiteSpace(vm.Password))
            {
                ModelState.AddModelError("", "Usuario y contraseña son obligatorios.");
                return View(vm);
            }

            var client = _httpClientFactory.CreateClient(nameof(AccountController));
            var baseUrl = _cfg["Api:BaseUrl"] ?? "https://nondeclaratory-brecken-unperpendicularly.ngrok-free.dev/";
            if (client.BaseAddress == null) client.BaseAddress = new Uri(baseUrl);

            // sin headers de empresa/token en el login
            client.DefaultRequestHeaders.Remove("X-Company-Id");

            var body = new { UserName = vm.Email, password = vm.Password, loginApp = true };
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            HttpResponseMessage resp;
            try
            {
                resp = await client.PostAsync("api/auth/login", content);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"No se pudo contactar la API: {ex.Message}");
                return View(vm);
            }

            if (!resp.IsSuccessStatusCode)
            {
                var reason = await resp.Content.ReadAsStringAsync();
                ModelState.AddModelError("", $"Credenciales inválidas o error en API. ({(int)resp.StatusCode}) {reason}");
                return View(vm);
            }

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tokenRaw = FindToken(root);
            var token = CleanupToken(tokenRaw);
            if (string.IsNullOrWhiteSpace(token))
            {
                ModelState.AddModelError("", "La API no devolvió un token válido.");
                return View(vm);
            }

            var userEl = FindUser(root);

            // ======= ID como string o int =======
            string id =
                GetStringCI(userEl, "id", "Id", "usuarioId", "userId")
                ?? (GetIntCI(userEl, "id", "Id", "usuarioId", "userId")?.ToString() ?? "");

            string name = GetStringCI(userEl, "nombre", "Nombre", "name", "Name", "nombreUsuario", "usuario") ?? "";
            string email = GetStringCI(userEl, "correo", "Correo", "email", "Email") ?? "";

            string rawRole = GetStringCI(userEl, "role", "Role", "perfil", "Perfil") ?? "";
            int? idProfile = GetIntCI(userEl, "idPerfil", "IdPerfil", "perfilId", "PerfilId");
            if (string.IsNullOrWhiteSpace(rawRole) && idProfile.HasValue)
                rawRole = idProfile.Value switch { 3 => "SuperAdmin", 2 => "Admin", 1 => "Agente", _ => "" };
            var role = NormalizarRol(rawRole);

            int companyId = GetIntCI(userEl, "companyId", "CompanyId", "company_id", "CompanyID") ?? 0;
            if (companyId <= 0) companyId = TryGetEmpresaIdFromJwt(token) ?? 0;

            string companyName = GetStringCI(userEl, "company", "Company") ?? "";

            // guardar en Session
            _accessor.HttpContext!.Session.SetString("JWT_TOKEN", token);
            _accessor.HttpContext!.Session.SetString("COMPANY_ID", companyId > 0 ? companyId.ToString() : "");
            _accessor.HttpContext!.Session.SetString("COMPANY_NAME", companyName ?? "");

            // cookie auth para la Web App
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, id),
                new Claim(ClaimTypes.Name, name),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Role, role),
                new Claim("role", role),
                new Claim("company_id", companyId.ToString()),
                new Claim("company", companyName ?? ""),
                new Claim("jwt", token)
            };

            var identity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme,
                ClaimTypes.Name,
                ClaimTypes.Role
            );

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    AllowRefresh = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2)
                });

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "Dashboard");
        }

        private static string NormalizarRol(string? raw)
        {
            var s = (raw ?? "").Trim();
            var flat = s.Replace(" ", "", StringComparison.OrdinalIgnoreCase);
            if (flat.Equals("superadmin", StringComparison.OrdinalIgnoreCase)) return "SuperAdmin";
            if (flat.Equals("admin", StringComparison.OrdinalIgnoreCase) || flat.Equals("administrador", StringComparison.OrdinalIgnoreCase)) return "Admin";
            if (flat.Equals("agente", StringComparison.OrdinalIgnoreCase) || flat.Equals("agent", StringComparison.OrdinalIgnoreCase)) return "Agente";
            return "Usuario";
        }

        // ====================== LOGOUT ======================

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            _accessor.HttpContext!.Session.Remove("JWT_TOKEN");
            _accessor.HttpContext!.Session.Remove("COMPANY_ID");
            _accessor.HttpContext!.Session.Remove("COMPANY_NAME");
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        [AllowAnonymous]
        public IActionResult Denied() => Content("Acceso denegado.");

        // ====================== PERFIL + CONTRASEÑA ======================

        public class ProfileVm
        {
            public int Id { get; set; }

            [Required(ErrorMessage = "El nombre es obligatorio.")]
            [Display(Name = "Nombre")]
            public string Name { get; set; } = string.Empty;

            [Required(ErrorMessage = "El correo es obligatorio.")]
            [EmailAddress]
            [Display(Name = "Correo electrónico")]
            public string Email { get; set; } = string.Empty;

            [Display(Name = "Empresa")]
            public string? Company { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "Contraseña actual")]
            public string? CurrentPassword { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "Nueva contraseña")]
            public string? NewPassword { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "Confirmar nueva contraseña")]
            public string? ConfirmPassword { get; set; }
        }

        private ApiService CreateApi()
        {
            var baseUrl = _cfg["Api:BaseUrl"] ?? "https://nondeclaratory-brecken-unperpendicularly.ngrok-free.dev/";
            var http = _httpClientFactory.CreateClient(nameof(ApiService));
            if (http.BaseAddress == null)
                http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            var companyId = _cfg["Api:CompanyId"] ?? "1";
            return new ApiService(http, companyId, _accessor);
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int id = 0;
            int.TryParse(idStr, out id);

            var api = CreateApi();
            UserDto? user = null;
            if (id > 0)
                user = await api.GetUsuarioByIdAsync(id);

            var vm = new ProfileVm
            {
                Id = id,
                Name = user?.Name ?? User.Identity?.Name ?? "",
                Email = user?.Email ?? User.FindFirstValue(ClaimTypes.Email) ?? "",
                Company = user?.Company ?? User.FindFirstValue("company")
            };

            return View(vm);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(ProfileVm vm)
        {
            var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int id = 0;
            int.TryParse(idStr, out id);
            vm.Id = id;

            // Solo consideramos cambio de contraseña si el usuario escribió Nueva o Confirmar
            var wantsPasswordChange =
                !string.IsNullOrWhiteSpace(vm.NewPassword) ||
                !string.IsNullOrWhiteSpace(vm.ConfirmPassword);

            if (wantsPasswordChange)
            {
                if (string.IsNullOrWhiteSpace(vm.NewPassword))
                    ModelState.AddModelError(nameof(vm.NewPassword), "La nueva contraseña es obligatoria.");

                if (string.IsNullOrWhiteSpace(vm.ConfirmPassword))
                    ModelState.AddModelError(nameof(vm.ConfirmPassword), "Debe confirmar la nueva contraseña.");

                if (!string.IsNullOrWhiteSpace(vm.NewPassword) && vm.NewPassword!.Length < 6)
                    ModelState.AddModelError(nameof(vm.NewPassword), "La nueva contraseña debe tener al menos 6 caracteres.");

                if (!string.Equals(vm.NewPassword, vm.ConfirmPassword))
                    ModelState.AddModelError(nameof(vm.ConfirmPassword), "La confirmación no coincide.");

                if (string.IsNullOrWhiteSpace(vm.CurrentPassword))
                    ModelState.AddModelError(nameof(vm.CurrentPassword), "La contraseña actual es obligatoria para cambiarla.");
            }

            if (!ModelState.IsValid)
                return View(vm);

            var api = CreateApi();

            var okPerfil = await api.UpdatePerfilUsuarioAsync(
                vm.Id,
                vm.Name?.Trim() ?? string.Empty,
                vm.Email?.Trim() ?? string.Empty,
                vm.Company?.Trim() ?? string.Empty
            );

            if (!okPerfil)
            {
                ModelState.AddModelError(string.Empty, "No se pudo actualizar el perfil. Inténtelo de nuevo.");
                return View(vm);
            }

            if (wantsPasswordChange && vm.Id > 0)
            {
                var result = await api.ChangePasswordAsync(vm.Id, vm.CurrentPassword!, vm.NewPassword!);
                if (!result.ok)
                {
                    ModelState.AddModelError(string.Empty, result.error ?? "No se pudo cambiar la contraseña.");
                    return View(vm);
                }
            }

            // refrescar claims para que el header use los nuevos datos
            try
            {
                if (User.Identity is ClaimsIdentity identity)
                {
                    void ReplaceClaim(string type, string? value)
                    {
                        if (string.IsNullOrWhiteSpace(value)) return;
                        var existing = identity.FindFirst(type);
                        if (existing != null && existing.Value != value)
                        {
                            identity.RemoveClaim(existing);
                            identity.AddClaim(new Claim(type, value));
                        }
                    }

                    ReplaceClaim(ClaimTypes.Name, vm.Name);
                    ReplaceClaim(ClaimTypes.Email, vm.Email);
                    ReplaceClaim("company", vm.Company);

                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        new ClaimsPrincipal(identity),
                        new AuthenticationProperties
                        {
                            IsPersistent = true,
                            AllowRefresh = true,
                            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2)
                        });
                }
            }
            catch
            {
                // si algo falla, no rompemos el flujo
            }

            ViewData["Success"] = wantsPasswordChange
                ? "Perfil y contraseña actualizados correctamente."
                : "Perfil actualizado correctamente.";

            vm.CurrentPassword = vm.NewPassword = vm.ConfirmPassword = string.Empty;

            return View(vm);
        }

        // ================ Helpers JSON/token ==================

        private static string CleanupToken(string? t)
        {
            if (string.IsNullOrWhiteSpace(t)) return "";
            var s = t.Trim();
            if (s.StartsWith("\"") && s.EndsWith("\"")) s = s.Trim('"');
            if (s.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) s = s.Substring(7).Trim();
            return s;
        }

        private static string? FindToken(JsonElement root)
        {
            if (TryGetCI(root, "token", out var v) && v.ValueKind == JsonValueKind.String) return v.GetString();

            foreach (var key in new[] { "data", "Data", "objeto", "Objeto", "result", "Result" })
                if (TryGetCI(root, key, out var node) && node.ValueKind == JsonValueKind.Object)
                    if (TryGetCI(node, "token", out var t) && t.ValueKind == JsonValueKind.String) return t.GetString();

            foreach (var p in root.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Object)
                    if (TryGetCI(p.Value, "token", out var t2) && t2.ValueKind == JsonValueKind.String) return t2.GetString();

            return null;
        }

        private static JsonElement FindUser(JsonElement root)
        {
            if (TryGetCI(root, "user", out var u) && u.ValueKind == JsonValueKind.Object) return u;
            if (TryGetCI(root, "usuario", out var u2) && u2.ValueKind == JsonValueKind.Object) return u2;

            foreach (var key in new[] { "data", "Data", "objeto", "Objeto", "result", "Result" })
                if (TryGetCI(root, key, out var node) && node.ValueKind == JsonValueKind.Object)
                {
                    if (TryGetCI(node, "user", out var d1) && d1.ValueKind == JsonValueKind.Object) return d1;
                    if (TryGetCI(node, "usuario", out var d2) && d2.ValueKind == JsonValueKind.Object) return d2;
                }

            return root; // campos planos
        }

        private static bool TryGetCI(JsonElement obj, string name, out JsonElement value)
        {
            if (obj.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in obj.EnumerateObject())
                    if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    { value = p.Value; return true; }
            }
            value = default; return false;
        }

        private static string? GetStringCI(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
                if (TryGetCI(obj, n, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString();
            return null;
        }

        private static int? GetIntCI(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
            {
                if (TryGetCI(obj, n, out var v))
                {
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
                    if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var i2)) return i2;
                }
            }
            return null;
        }

        private static int? TryGetEmpresaIdFromJwt(string jwt)
        {
            try
            {
                var parts = jwt.Split('.');
                if (parts.Length < 2) return null;
                string payload = parts[1]
                    .Replace('-', '+')
                    .Replace('_', '/');
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                foreach (var k in new[] { "company_id", "CompanyId", "companyId", "empresa_id" })
                {
                    if (root.TryGetProperty(k, out var v))
                    {
                        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
                        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
