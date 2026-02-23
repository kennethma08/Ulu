using System.IdentityModel.Tokens.Jwt;

namespace WhatsappClient.Helpers
{
    public static class JwtHelper
    {
        public static DateTimeOffset? GetJwtExpiry(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            try
            {
                var handler = new JwtSecurityTokenHandler();
                if (handler.CanReadToken(token))
                {
                    var jwt = handler.ReadJwtToken(token);
                    // ValidTo is DateTime in UTC.
                    return new DateTimeOffset(jwt.ValidTo);
                }
            }
            catch { }

            return null;
        }
    }
}
