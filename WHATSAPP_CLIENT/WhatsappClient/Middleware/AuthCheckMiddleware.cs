using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using WhatsappClient.Helpers;

namespace WhatsappClient.Middleware
{
    public class AuthCheckMiddleware
    {
        private readonly RequestDelegate _next;

        public AuthCheckMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext ctx)
        {
            string? token = ctx.Session.GetString("JWT_TOKEN");
            if (!string.IsNullOrWhiteSpace(token))
            {
                token = token.Trim();
                if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    token = token[7..].Trim();
                if (token.StartsWith("\"") && token.EndsWith("\""))
                    token = token.Trim('"');

                var exp = JwtHelper.GetJwtExpiry(token);
                if (exp != null && DateTimeOffset.UtcNow >= exp.Value.AddSeconds(-60))
                {
                    try { ctx.Session.Remove("JWT_TOKEN"); } catch { }
                    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                    if (!ctx.Request.Path.StartsWithSegments("/Account", StringComparison.OrdinalIgnoreCase))
                    {
                        var returnUrl = Uri.EscapeDataString(ctx.Request.Path + ctx.Request.QueryString);
                        ctx.Response.Redirect("/Account/Login?returnUrl=" + returnUrl);
                        return;
                    }
                }
            }

            await _next(ctx);
        }
    }
}
