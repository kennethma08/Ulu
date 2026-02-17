using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Whatsapp_API.Helpers;

namespace Whatsapp_API.Models.Helpers
{
    public static class WhatsappMediaHelper
    {
        public sealed class WhatsAppMediaInfo
        {
            public string Type { get; set; } = "";
            public string MediaId { get; set; } = "";
            public string? MimeType { get; set; }
            public string? FileName { get; set; }
            public long? SizeBytes { get; set; }
            public bool IsVoice { get; set; }
        }

        /// <summary>
        /// Lee el JSON de Meta y devuelve info de audio / imagen / documento / video.
        /// </summary>
        public static WhatsAppMediaInfo? ExtractMediaInfo(JsonElement m)
        {
            try
            {
                if (!m.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String)
                    return null;

                var type = t.GetString() ?? string.Empty;

                // Solo nos interesan estos tipos
                if (!string.Equals(type, "audio", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(type, "document", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(type, "image", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(type, "video", StringComparison.OrdinalIgnoreCase))
                    return null;

                if (!m.TryGetProperty(type, out var obj) || obj.ValueKind != JsonValueKind.Object)
                    return null;

                if (!obj.TryGetProperty("id", out var idProp) || idProp.ValueKind != JsonValueKind.String)
                    return null;

                var mediaId = idProp.GetString();
                if (string.IsNullOrWhiteSpace(mediaId))
                    return null;

                string? mime = null;
                if (obj.TryGetProperty("mime_type", out var mt) && mt.ValueKind == JsonValueKind.String)
                    mime = mt.GetString();

                long? sizeBytes = null;
                if (obj.TryGetProperty("file_size", out var sz) && sz.ValueKind == JsonValueKind.Number &&
                    sz.TryGetInt64(out var s))
                    sizeBytes = s;

                string? fileName = null;
                if (obj.TryGetProperty("filename", out var fn) && fn.ValueKind == JsonValueKind.String)
                    fileName = fn.GetString();

                bool isVoice = false;
                if (string.Equals(type, "audio", StringComparison.OrdinalIgnoreCase) &&
                    obj.TryGetProperty("voice", out var voice) &&
                    (voice.ValueKind == JsonValueKind.True || voice.ValueKind == JsonValueKind.False))
                {
                    isVoice = voice.GetBoolean();
                }

                return new WhatsAppMediaInfo
                {
                    Type = type,
                    MediaId = mediaId!,
                    MimeType = mime,
                    FileName = fileName,
                    SizeBytes = sizeBytes,
                    IsVoice = isVoice
                };
            }
            catch (Exception ex)
            {
                SimpleFileLogger.LogJson("whatsapp-media", "EXTRACT-ERR", ex.ToString());
                return null;
            }
        }

        /// <summary>
        /// Devuelve extensión sugerida según el MIME.
        /// </summary>
        public static string GuessExtension(string? mime)
        {
            if (string.IsNullOrWhiteSpace(mime)) return "";
            mime = mime.ToLowerInvariant();

            if (mime.StartsWith("audio/mpeg")) return ".mp3";
            if (mime.StartsWith("audio/aac")) return ".aac";
            if (mime.StartsWith("audio/amr")) return ".amr";
            if (mime.StartsWith("audio/ogg")) return ".ogg";
            if (mime.StartsWith("audio/mp4") || mime.StartsWith("audio/m4a")) return ".m4a";

            if (mime.StartsWith("image/jpeg")) return ".jpg";
            if (mime.StartsWith("image/png")) return ".png";

            if (mime.StartsWith("video/mp4")) return ".mp4";
            if (mime.StartsWith("application/pdf")) return ".pdf";

            return "";
        }

        /// <summary>
        /// Descarga el binario de un media_id de la Cloud API de WhatsApp.
        /// </summary>
        public static async Task<byte[]?> DownloadWhatsAppMediaAsync(
            string accessToken,
            string mediaId,
            string baseUrl,
            string apiVersion,
            CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(accessToken))
                    return null;

                baseUrl = (baseUrl ?? "https://graph.facebook.com").TrimEnd('/');
                apiVersion = string.IsNullOrWhiteSpace(apiVersion) ? "v20.0" : apiVersion;

                var metaUrl = $"{baseUrl}/{apiVersion}/{mediaId}";

                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromSeconds(60);
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                // 1) Obtener URL real del archivo
                var metaResp = await http.GetAsync(metaUrl, ct);
                if (!metaResp.IsSuccessStatusCode)
                {
                    SimpleFileLogger.Log("whatsapp-media", "META-ERR",
                        $"status={(int)metaResp.StatusCode} media={mediaId}");
                    return null;
                }

                using var metaDoc = JsonDocument.Parse(await metaResp.Content.ReadAsStringAsync(ct));
                if (!metaDoc.RootElement.TryGetProperty("url", out var urlProp) ||
                    urlProp.ValueKind != JsonValueKind.String)
                {
                    SimpleFileLogger.Log("whatsapp-media", "META-NO-URL", $"media={mediaId}");
                    return null;
                }

                var fileUrl = urlProp.GetString();
                if (string.IsNullOrWhiteSpace(fileUrl))
                    return null;

                // 2) Descargar el binario
                var fileResp = await http.GetAsync(fileUrl, ct);
                if (!fileResp.IsSuccessStatusCode)
                {
                    SimpleFileLogger.Log("whatsapp-media", "DOWNLOAD-ERR",
                        $"status={(int)fileResp.StatusCode} media={mediaId}");
                    return null;
                }

                return await fileResp.Content.ReadAsByteArrayAsync(ct);
            }
            catch (Exception ex)
            {
                SimpleFileLogger.LogJson("whatsapp-media", "DOWNLOAD-EX", ex.ToString());
                return null;
            }
        }
    }
}
