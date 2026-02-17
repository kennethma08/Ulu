using Microsoft.AspNetCore.Builder;

namespace Whatsapp_API.Infrastructure.MultiTenancy
{
    public static class TenantResolverExtensions
    {
        public static IApplicationBuilder UseTenantResolver(this IApplicationBuilder app)
            => app.UseMiddleware<TenantResolverMiddleware>();
    }
}
