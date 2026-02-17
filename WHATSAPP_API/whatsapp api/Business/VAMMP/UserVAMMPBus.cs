using System.Net.Http.Headers;
using System.Text.Json;
using Whatsapp_API.Models.Helpers;
using Whatsapp_API.Models.Request.VAMMP;

namespace Whatsapp_API.Business.VAMMP
{
    /// <summary>
    /// Lógica de consumo del API de VAMMP para consultar usuarios (igual patrón que el original).
    /// Requiere configuración "VAMMP:BaseUrl" y opcionalmente el endpoint "VAMMP:UsuariosByCorreoPath".
    /// </summary>
    public class UserVAMMPBus
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _byCorreoPath;

        public UserVAMMPBus(IConfiguration cfg, IHttpClientFactory httpFactory)
        {
            _http = httpFactory.CreateClient(nameof(UserVAMMPBus));
            _baseUrl = cfg.GetValue<string>("VAMMP:BaseUrl") ?? "";
            _byCorreoPath = cfg.GetValue<string>("VAMMP:UsuariosByCorreoPath") ?? "/api/usuarios/by-email";
            if (!string.IsNullOrWhiteSpace(_baseUrl))
                _http.BaseAddress = new Uri(_baseUrl);
        }

        /// <summary>
        /// Consulta un usuario en VAMMP por correo. Envíe token (Bearer) que obtuviste con Auth2.
        /// </summary>
        public async Task<BooleanoDescriptivo<UserVAMMP>> ConsultaUsuarioVammp(string token, string correo, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_baseUrl))
            {
                return new BooleanoDescriptivo<UserVAMMP>
                {
                    Exitoso = false,
                    Mensaje = "VAMMP:BaseUrl no configurado",
                    StatusCode = 500
                };
            }

            try
            {
                var url = $"{_byCorreoPath}?correo={Uri.EscapeDataString(correo)}";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (!string.IsNullOrWhiteSpace(token))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var res = await _http.SendAsync(req, ct);
                var json = await res.Content.ReadAsStringAsync(ct);

                if (!res.IsSuccessStatusCode)
                {
                    return new BooleanoDescriptivo<UserVAMMP>
                    {
                        Exitoso = false,
                        Mensaje = $"VAMMP {(int)res.StatusCode}: {json}",
                        StatusCode = (int)res.StatusCode
                    };
                }

                var dto = JsonSerializer.Deserialize<UserVAMMP>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return new BooleanoDescriptivo<UserVAMMP>
                {
                    Exitoso = dto != null,
                    Data = dto,
                    Mensaje = dto != null ? "OK" : "Respuesta vacía",
                    StatusCode = 200
                };
            }
            catch (Exception ex)
            {
                return new BooleanoDescriptivo<UserVAMMP>
                {
                    Exitoso = false,
                    Mensaje = "Excepción al consultar VAMMP: " + ex.Message,
                    StatusCode = 500
                };
            }
        }
    }
}
