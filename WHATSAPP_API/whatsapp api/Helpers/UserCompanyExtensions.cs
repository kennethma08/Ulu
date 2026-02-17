using System.Security.Claims;

namespace Whatsapp_API.Helpers  
{
    public static class UserCompanyExtensions
    {
        // saca el id de empresa desde los claims del usuario
        public static int? GetEmpresaId(this ClaimsPrincipal? user)
        {
            if (user == null) return null;

            var s =
                user.FindFirst("company_id")?.Value ??
                user.FindFirst("empresaId")?.Value ??
                user.FindFirst("empresa_id")?.Value;

            return int.TryParse(s, out var id) ? id : (int?)null;
        }
    }
}
