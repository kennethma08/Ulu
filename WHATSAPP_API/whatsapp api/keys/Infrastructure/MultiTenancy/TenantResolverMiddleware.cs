using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Whatsapp_API.Data;

namespace Whatsapp_API.Infrastructure.MultiTenancy
{
    public class TenantResolverMiddleware
    {
        private readonly RequestDelegate _next;
        public TenantResolverMiddleware(RequestDelegate next) => _next = next;

        public async Task Invoke(HttpContext ctx, TenantContext tenant, MyDbContext db)
        {
            // 1) Si viene header explícito (útil para pruebas desde Swagger)
            if (TryGetEmpresaFromHeader(ctx, out var eidFromHeader) && eidFromHeader > 0)
            {
                tenant.CompanyId = eidFromHeader;
                ctx.Items["COMPANY_ID"] = eidFromHeader;  // <- clave: visibile para los Bus
                await _next(ctx);
                return;
            }

            // 2) Webhook: resolver por phone_number_id del payload
            if (IsWebhook(ctx))
            {
                await ResolverPorWebhook(ctx, tenant, db);
                await _next(ctx);
                return;
            }

            // 3) Tráfico autenticado: resolver por claims o BD
            await ResolverPorUsuario(ctx, tenant, db);
            await _next(ctx);
        }

        private static bool TryGetEmpresaFromHeader(HttpContext ctx, out int empresaId)
        {
            empresaId = 0;
            if (ctx.Request.Headers.TryGetValue("X-Empresa-Id", out var h1)
                && int.TryParse(h1.ToString(), out var v1))
            {
                empresaId = v1;
                return empresaId > 0;
            }
            return false;
        }

        private static bool IsWebhook(HttpContext ctx)
        {
            var p = ctx.Request.Path.Value ?? "";
            return p.IndexOf("webhook", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static async Task ResolverPorWebhook(HttpContext ctx, TenantContext tenant, MyDbContext db)
        {
            if (!HttpMethods.Post.Equals(ctx.Request.Method, StringComparison.OrdinalIgnoreCase)) return;

            ctx.Request.EnableBuffering();
            using var sr = new StreamReader(ctx.Request.Body, System.Text.Encoding.UTF8, leaveOpen: true);
            var raw = await sr.ReadToEndAsync();
            ctx.Request.Body.Position = 0;

            if (string.IsNullOrWhiteSpace(raw)) return;

            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                // primero metadata.phone_number_id
                string? phoneId = TryGetPhoneIdMeta(root);
                // si no, buscar phone_number_id en todo el JSON (por si Meta cambia el formato)
                phoneId ??= FindStringByKey(root, "phone_number_id");

                if (!string.IsNullOrWhiteSpace(phoneId))
                {
                    var integ = await db.Integrations
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.IsActive && x.PhoneNumberId == phoneId);

                    if (integ != null && integ.CompanyId > 0)
                    {
                        tenant.CompanyId = integ.CompanyId;
                        ctx.Items["COMPANY_ID"] = integ.CompanyId; // <- MUY IMPORTANTE
                        return;
                    }
                }
            }
            catch
            {
                // swallow
            }
        }

        private static string? TryGetPhoneIdMeta(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("entry", out var entry) || entry.ValueKind != JsonValueKind.Array) return null;

            foreach (var e in entry.EnumerateArray())
            {
                if (!e.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) continue;
                foreach (var ch in changes.EnumerateArray())
                {
                    if (!ch.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object) continue;
                    if (value.TryGetProperty("metadata", out var meta) && meta.ValueKind == JsonValueKind.Object)
                    {
                        if (meta.TryGetProperty("phone_number_id", out var pn) && pn.ValueKind == JsonValueKind.String)
                            return pn.GetString();
                    }
                }
            }
            return null;
        }

        private static string? FindStringByKey(JsonElement el, string key)
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in el.EnumerateObject())
                {
                    if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)
                        && p.Value.ValueKind == JsonValueKind.String)
                        return p.Value.GetString();
                    var hit = FindStringByKey(p.Value, key);
                    if (hit != null) return hit;
                }
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in el.EnumerateArray())
                {
                    var hit = FindStringByKey(it, key);
                    if (hit != null) return hit;
                }
            }
            return null;
        }

        private static async Task ResolverPorUsuario(HttpContext ctx, TenantContext tenant, MyDbContext db)
        {
            if (ctx.User?.Identity?.IsAuthenticated != true) return;

            var empClaim = ctx.User.FindFirst("company_id")?.Value
                           ?? ctx.User.FindFirst("CompanyId")?.Value;

            if (int.TryParse(empClaim, out var eidFromClaim) && eidFromClaim > 0)
            {
                tenant.CompanyId = eidFromClaim;
                ctx.Items["COMPANY_ID"] = eidFromClaim; // uniformar
                return;
            }

            var userIdClaim =
                   ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? ctx.User.FindFirst("sub")?.Value
                ?? ctx.User.FindFirst("user_id")?.Value
                ?? ctx.User.FindFirst("id")?.Value;

            if (!int.TryParse(userIdClaim, out var uid)) return;

            var u = await db.Users.AsNoTracking()
                     .Where(x => x.Id == uid)
                     .Select(x => new { x.CompanyId })
                     .FirstOrDefaultAsync();

            if (u?.CompanyId != null && u.CompanyId.Value > 0)
            {
                tenant.CompanyId = u.CompanyId.Value;
                ctx.Items["COMPANY_ID"] = u.CompanyId.Value;
            }
        }
    }
}
