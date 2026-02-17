using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Whatsapp_API.Models.Helpers;
using Whatsapp_API.Models.Request.Auth2;

namespace Whatsapp_API.Business.VAMMP
{
    /// <summary>
    /// Maneja el flujo OAuth2 para obtener/renovar tokens (como el original).
    /// Lee su configuración desde appsettings: sección "Auth2".
    /// </summary>
    public class Auth2Bus
    {
        private readonly OAuth2Config _cfg;
        private readonly HttpClient _http;

        public Auth2Bus(IConfiguration configuration, IHttpClientFactory httpFactory)
        {
            _cfg = configuration.GetSection("Auth2").Get<OAuth2Config>() ?? new OAuth2Config();
            _http = httpFactory.CreateClient(nameof(Auth2Bus));
            if (!string.IsNullOrWhiteSpace(_cfg.TokenUrl))
                _http.BaseAddress = new Uri(_cfg.TokenUrl);
        }

        /// <summary>
        /// Construye la URL de autorización (si tu proveedor OAuth2 expone una AuthUrl).
        /// </summary>
        public string GetAuthorizeUrl(string? state = null, string? scope = null)
        {
            if (string.IsNullOrWhiteSpace(_cfg.AuthUrl))
                return string.Empty;

            var sb = new StringBuilder();
            sb.Append(_cfg.AuthUrl);
            sb.Append(_cfg.AuthUrl!.Contains('?') ? "&" : "?");
            sb.Append("response_type=code");
            sb.Append($"&client_id={Uri.EscapeDataString(_cfg.ClientId ?? string.Empty)}");
            if (!string.IsNullOrWhiteSpace(_cfg.CallbackUrl))
                sb.Append($"&redirect_uri={Uri.EscapeDataString(_cfg.CallbackUrl!)}");
            if (!string.IsNullOrWhiteSpace(scope))
                sb.Append($"&scope={Uri.EscapeDataString(scope)}");
            if (!string.IsNullOrWhiteSpace(state))
                sb.Append($"&state={Uri.EscapeDataString(state)}");

            return sb.ToString();
        }

        /// <summary>
        /// Intercambia el authorization code por el access_token (grant_type=authorization_code).
        /// </summary>
        public async Task<BooleanoDescriptivo<TokenResponse>> ExchangeCodeForTokenAsync(string code, CancellationToken ct = default)
        {
            var form = new Dictionary<string, string?>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["client_id"] = _cfg.ClientId,
                ["client_secret"] = _cfg.ClientSecret,
                ["redirect_uri"] = _cfg.CallbackUrl
            };

            return await PostTokenAsync(form, ct);
        }

        /// <summary>
        /// Renueva el access_token a partir del refresh_token (grant_type=refresh_token).
        /// </summary>
        public async Task<BooleanoDescriptivo<TokenResponse>> RefreshAsync(string refreshToken, CancellationToken ct = default)
        {
            var form = new Dictionary<string, string?>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = _cfg.ClientId,
                ["client_secret"] = _cfg.ClientSecret
            };

            return await PostTokenAsync(form, ct);
        }

        private async Task<BooleanoDescriptivo<TokenResponse>> PostTokenAsync(Dictionary<string, string?> form, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_cfg.TokenUrl))
            {
                return new BooleanoDescriptivo<TokenResponse>
                {
                    Exitoso = false,
                    Mensaje = "Auth2.TokenUrl no configurado",
                    StatusCode = 500
                };
            }

            using var req = new HttpRequestMessage(HttpMethod.Post, _cfg.TokenUrl)
            {
                Content = new FormUrlEncodedContent(form!
                    .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                    .ToDictionary(kv => kv.Key, kv => kv.Value!))
            };
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            try
            {
                var res = await _http.SendAsync(req, ct);
                var json = await res.Content.ReadAsStringAsync(ct);

                if (!res.IsSuccessStatusCode)
                {
                    return new BooleanoDescriptivo<TokenResponse>
                    {
                        Exitoso = false,
                        Mensaje = $"OAuth2 error {(int)res.StatusCode}: {json}",
                        StatusCode = (int)res.StatusCode
                    };
                }

                var token = JsonSerializer.Deserialize<TokenResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return new BooleanoDescriptivo<TokenResponse>
                {
                    Exitoso = token != null,
                    Data = token,
                    Mensaje = token != null ? "OK" : "Respuesta vacía",
                    StatusCode = 200
                };
            }
            catch (Exception ex)
            {
                return new BooleanoDescriptivo<TokenResponse>
                {
                    Exitoso = false,
                    Mensaje = "Excepción en OAuth2: " + ex.Message,
                    StatusCode = 500
                };
            }
        }
    }
}
