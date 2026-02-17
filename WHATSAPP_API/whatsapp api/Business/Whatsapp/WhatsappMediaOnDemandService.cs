using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Whatsapp_API.Data;
using Whatsapp_API.Models.Entities.System; // Integration

namespace Whatsapp_API.Business.Whatsapp
{
    public class WhatsappMediaOnDemandService
    {
        private readonly MyDbContext _db;
        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<WhatsappMediaOnDemandService> _logger;

        public WhatsappMediaOnDemandService(
            MyDbContext db,
            IHttpClientFactory httpFactory,
            ILogger<WhatsappMediaOnDemandService> logger)
        {
            _db = db;
            _httpFactory = httpFactory;
            _logger = logger;
        }

        /// <summary>
        /// Descarga el binario de un media de WhatsApp usando whatsapp_media_id y la integración activa.
        /// NO guarda en BD, solo devuelve bytes + mimeType.
        /// </summary>
        public async Task<(byte[]? bytes, string? mimeType, string? error)> DownloadMediaAsync(int companyId, string whatsappMediaId)
        {
            if (string.IsNullOrWhiteSpace(whatsappMediaId))
                return (null, null, "whatsapp_media_id vacío");

            // 1) Buscar integración activa para la empresa
            var integ = await _db.Integrations
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.CompanyId == companyId && i.IsActive);

            if (integ == null)
                return (null, null, "No hay integración activa para la empresa");

            var accessToken = DecodeAccessToken(integ);
            if (string.IsNullOrWhiteSpace(accessToken))
                return (null, null, "La integración no tiene AccessToken válido (AccessTokenEnc vacío o no se pudo decodificar).");

            var apiBase = string.IsNullOrWhiteSpace(integ.ApiBaseUrl)
                ? "https://graph.facebook.com"
                : integ.ApiBaseUrl;

            var apiVersion = string.IsNullOrWhiteSpace(integ.ApiVersion)
                ? "v20.0"
                : integ.ApiVersion;

            var http = _httpFactory.CreateClient();

            try
            {
                // ===== PASO 1: Metadata del media =====
                var metaUrl = $"{apiBase.TrimEnd('/')}/{apiVersion}/{whatsappMediaId}";
                _logger.LogInformation("[WhatsappMediaOnDemand] GET {MetaUrl}", metaUrl);

                using (var metaReq = new HttpRequestMessage(HttpMethod.Get, metaUrl))
                {
                    metaReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                    var metaRes = await http.SendAsync(metaReq);
                    var metaBody = await metaRes.Content.ReadAsStringAsync();

                    if (!metaRes.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("[WhatsappMediaOnDemand] Meta status={Status} body={Body}",
                            (int)metaRes.StatusCode, metaBody);
                        return (null, null, $"Meta API {(int)metaRes.StatusCode}: {metaBody}");
                    }

                    using var jd = JsonDocument.Parse(metaBody);
                    var root = jd.RootElement;

                    var downloadUrl = root.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String
                        ? urlEl.GetString()
                        : null;

                    var mimeType = root.TryGetProperty("mime_type", out var mtEl) && mtEl.ValueKind == JsonValueKind.String
                        ? mtEl.GetString()
                        : null;

                    if (string.IsNullOrWhiteSpace(downloadUrl))
                        return (null, null, "Meta no devolvió url de descarga para el media.");

                    // ===== PASO 2: Descargar binario =====
                    _logger.LogInformation("[WhatsappMediaOnDemand] GET binary {DownloadUrl}", downloadUrl);

                    using var binReq = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                    binReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                    var binRes = await http.SendAsync(binReq);
                    if (!binRes.IsSuccessStatusCode)
                    {
                        var body = await binRes.Content.ReadAsStringAsync();
                        _logger.LogWarning("[WhatsappMediaOnDemand] Binary status={Status} body={Body}",
                            (int)binRes.StatusCode, body);

                        // 404 normalmente = media expirado en Meta
                        return (null, null, $"No se pudo descargar el media (status {(int)binRes.StatusCode}). Es posible que haya expirado en Meta.");
                    }

                    var bytes = await binRes.Content.ReadAsByteArrayAsync();
                    var finalMime = !string.IsNullOrWhiteSpace(mimeType)
                        ? mimeType
                        : (binRes.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");

                    return (bytes, finalMime, null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WhatsappMediaOnDemand] Error descargando media {MediaId}", whatsappMediaId);
                return (null, null, ex.Message);
            }
        }

        /// <summary>
        /// Decodifica el access token desde AccessTokenEnc.
        /// IMPORTANTE: ajusta esta lógica a tu mecanismo real de cifrado.
        /// Ahora mismo asume que está guardado como UTF8 plano.
        /// </summary>
        private static string? DecodeAccessToken(Integration integ)
        {
            try
            {
                if (integ.AccessTokenEnc == null || integ.AccessTokenEnc.Length == 0)
                    return null;

                // Si en realidad está cifrado, aquí deberías llamar a tu helper:
                // return CryptoHelper.DecryptToString(integ.AccessTokenEnc);
                return Encoding.UTF8.GetString(integ.AccessTokenEnc);
            }
            catch
            {
                return null;
            }
        }
    }
}

