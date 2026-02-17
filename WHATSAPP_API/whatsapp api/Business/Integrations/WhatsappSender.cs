using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Whatsapp_API.Models.Helpers;

namespace Whatsapp_API.Business.Integrations
{
    // Record público accesible desde los controladores
    public record TemplateHeaderLocation(double Latitude, double Longitude, string? Name = null, string? Address = null);

    public class WhatsappSender
    {
        private readonly IHttpClientFactory _http;
        private readonly IntegrationBus _intBus;

        // Tipos de audio permitidos por la Cloud API
        private static readonly HashSet<string> AllowedAudioMimes =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            {
                "audio/aac",
                "audio/mp4",
                "audio/mpeg",
                "audio/amr",
                "audio/ogg",
                "audio/opus",
                "audio/mp3",
                "audio/x-m4a",
                "audio/m4a"
            };

        private static string CleanMime(string? mime)
        {
            if (string.IsNullOrWhiteSpace(mime)) return "audio/ogg";
            var s = mime.Trim();
            var semi = s.IndexOf(';');
            if (semi > 0) s = s.Substring(0, semi);
            return string.IsNullOrWhiteSpace(s) ? "audio/ogg" : s;
        }

        public WhatsappSender(IHttpClientFactory http, IntegrationBus intBus)
        {
            _http = http;
            _intBus = intBus;
        }

        public async Task<BooleanoDescriptivo<object>> CheckAsync()
        {
            var (ok, token, baseUrl, version, phoneId, err) = _intBus.GetDecryptedForSend();
            if (!ok)
                return new BooleanoDescriptivo<object> { Exitoso = false, Mensaje = err ?? "Config", StatusCode = 400 };

            var url = $"{baseUrl}/{version}/{phoneId}?fields=verified_name,display_phone_number";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var cli = _http.CreateClient(nameof(WhatsappSender));
            var res = await cli.SendAsync(req);
            var content = await res.Content.ReadAsStringAsync();

            return new BooleanoDescriptivo<object>
            {
                Exitoso = res.IsSuccessStatusCode,
                Mensaje = res.IsSuccessStatusCode ? "OK" : content,
                StatusCode = (int)res.StatusCode,
                Data = res.IsSuccessStatusCode ? JsonSerializer.Deserialize<object>(content) : default
            };
        }

        public async Task<DescriptiveBoolean> SendTextAsync(string toPhoneE164, string text)
        {
            var (ok, token, baseUrl, version, phoneId, err) = _intBus.GetDecryptedForSend();
            if (!ok) return new DescriptiveBoolean { Exitoso = false, Mensaje = err ?? "Config", StatusCode = 400 };

            var url = $"{baseUrl}/{version}/{phoneId}/messages";
            var body = new
            {
                messaging_product = "whatsapp",
                to = toPhoneE164,
                type = "text",
                text = new { body = text }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var cli = _http.CreateClient(nameof(WhatsappSender));
            var res = await cli.SendAsync(req);
            var content = await res.Content.ReadAsStringAsync();

            return new DescriptiveBoolean
            {
                Exitoso = res.IsSuccessStatusCode,
                Mensaje = res.IsSuccessStatusCode ? "OK" : content,
                StatusCode = (int)res.StatusCode
            };
        }

        // envía un documento (PDF) por URL pública
        public async Task<DescriptiveBoolean> SendDocumentByUrlAsync(
            string toPhoneE164,
            string documentUrl,
            string? caption = null,
            string? filename = null)
        {
            var (ok, token, baseUrl, version, phoneId, err) = _intBus.GetDecryptedForSend();
            if (!ok) return new DescriptiveBoolean { Exitoso = false, Mensaje = err ?? "Config", StatusCode = 400 };

            var url = $"{baseUrl}/{version}/{phoneId}/messages";

            object documentPayload = filename is { Length: > 0 }
                ? new { link = documentUrl, caption, filename }
                : new { link = documentUrl, caption };

            var payload = new
            {
                messaging_product = "whatsapp",
                to = toPhoneE164,
                type = "document",
                document = documentPayload
            };

            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var cli = _http.CreateClient(nameof(WhatsappSender));
            var res = await cli.SendAsync(req);
            var content = await res.Content.ReadAsStringAsync();

            return new DescriptiveBoolean
            {
                Exitoso = res.IsSuccessStatusCode,
                Mensaje = res.IsSuccessStatusCode ? "OK" : content,
                StatusCode = (int)res.StatusCode
            };
        }

        // Envío de ubicación
        public async Task<DescriptiveBoolean> SendLocationAsync(
            string toPhoneE164, double latitude, double longitude, string? name = null, string? address = null)
        {
            var (ok, token, baseUrl, version, phoneId, err) = _intBus.GetDecryptedForSend();
            if (!ok) return new DescriptiveBoolean { Exitoso = false, Mensaje = err ?? "Config", StatusCode = 400 };

            var url = $"{baseUrl}/{version}/{phoneId}/messages";
            var payload = new
            {
                messaging_product = "whatsapp",
                to = toPhoneE164,
                type = "location",
                location = new { latitude, longitude, name, address }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var cli = _http.CreateClient(nameof(WhatsappSender));
            var res = await cli.SendAsync(req);
            var content = await res.Content.ReadAsStringAsync();

            return new DescriptiveBoolean
            {
                Exitoso = res.IsSuccessStatusCode,
                Mensaje = res.IsSuccessStatusCode ? "OK" : content,
                StatusCode = (int)res.StatusCode
            };
        }

        // Plantillas, fallback y demás métodos...
        public async Task<DescriptiveBoolean> SendTemplateAsync(
            string toPhoneE164, string templateName, string lang, List<string>? bodyVars = null, TemplateHeaderLocation? headerLocation = null)
        {
            var (ok, token, baseUrl, version, phoneId, err) = _intBus.GetDecryptedForSend();
            if (!ok) return new DescriptiveBoolean { Exitoso = false, Mensaje = err ?? "Config", StatusCode = 400 };

            var url = $"{baseUrl}/{version}/{phoneId}/messages";

            var components = new List<object>();

            if (headerLocation != null)
            {
                components.Add(new
                {
                    type = "header",
                    parameters = new object[]
                    {
                        new
                        {
                            type = "location",
                            location = new
                            {
                                latitude = headerLocation.Latitude,
                                longitude = headerLocation.Longitude,
                                name = headerLocation.Name,
                                address = headerLocation.Address
                            }
                        }
                    }
                });
            }

            if (bodyVars != null && bodyVars.Count > 0)
            {
                components.Add(new
                {
                    type = "body",
                    parameters = bodyVars.ConvertAll(v => new { type = "text", text = v }).ToArray()
                });
            }

            var payload = new
            {
                messaging_product = "whatsapp",
                to = toPhoneE164,
                type = "template",
                template = new
                {
                    name = templateName,
                    language = new { code = lang },
                    components = components.Count > 0 ? components.ToArray() : null
                }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var cli = _http.CreateClient(nameof(WhatsappSender));
            var res = await cli.SendAsync(req);
            var content = await res.Content.ReadAsStringAsync();

            return new DescriptiveBoolean
            {
                Exitoso = res.IsSuccessStatusCode,
                Mensaje = res.IsSuccessStatusCode ? "OK" : content,
                StatusCode = (int)res.StatusCode
            };
        }

        public async Task<(bool ok, string usedLang, int code, string body)> SendTemplateWithFallbackAsync(
            string toPhoneE164, string templateName, IEnumerable<string> langs, List<string>? bodyVars = null)
        {
            foreach (var lang in langs)
            {
                var r = await SendTemplateAsync(toPhoneE164, templateName, lang, bodyVars);
                if (r.Exitoso) return (true, lang, r.StatusCode, r.Mensaje ?? "");
            }
            return (false, "", 0, "no language worked");
        }

        public async Task<DescriptiveBoolean> SendImageByUrlAsync(
            string toPhoneE164,
            string imageUrl,
            string? caption = null)
        {
            var (ok, token, baseUrl, version, phoneId, err) = _intBus.GetDecryptedForSend();
            if (!ok) return new DescriptiveBoolean { Exitoso = false, Mensaje = err ?? "Config", StatusCode = 400 };

            var url = $"{baseUrl}/{version}/{phoneId}/messages";

            // Si no hay caption, omitimos la propiedad (WhatsApp falla si va como null en algunos casos)
            object imagePayload = caption == null
                ? new { link = imageUrl }
                : new { link = imageUrl, caption };

            var payload = new
            {
                messaging_product = "whatsapp",
                to = toPhoneE164,
                type = "image",
                image = imagePayload
            };

            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var cli = _http.CreateClient(nameof(WhatsappSender));
            var res = await cli.SendAsync(req);
            var content = await res.Content.ReadAsStringAsync();

            return new DescriptiveBoolean
            {
                Exitoso = res.IsSuccessStatusCode,
                Mensaje = res.IsSuccessStatusCode ? "OK" : content,
                StatusCode = (int)res.StatusCode
            };
        }

        /// <summary>
        /// Sube el audio a Meta y envía un mensaje de tipo "audio" al número indicado.
        /// Valida MIME y NO hace ninguna conversión (sin ffmpeg).
        /// </summary>
        public async Task<DescriptiveBoolean> SendAudioAsync(
            string toPhoneE164,
            Stream audioStream,
            string fileName,
            string contentType)
        {
            var (ok, token, baseUrl, version, phoneId, err) = _intBus.GetDecryptedForSend();
            if (!ok)
                return new DescriptiveBoolean { Exitoso = false, Mensaje = err ?? "Config", StatusCode = 400 };

            var cleanMime = CleanMime(contentType);

            // Si el tipo de audio no es soportado, devolvemos 415
            if (!AllowedAudioMimes.Contains(cleanMime))
            {
                return new DescriptiveBoolean
                {
                    Exitoso = false,
                    Mensaje =
                        $"Tipo de audio no soportado por WhatsApp: '{cleanMime}'. " +
                        "Use .ogg, .mp3, .aac, .amr, .m4a o similar.",
                    StatusCode = 415
                };
            }

            if (audioStream.CanSeek)
                audioStream.Position = 0;

            var cli = _http.CreateClient(nameof(WhatsappSender));

            // =======================
            // 1) SUBIR MEDIA (audio)
            // =======================
            var mediaUrl = $"{baseUrl}/{version}/{phoneId}/media";

            using var form = new MultipartFormDataContent();

            var fileContent = new StreamContent(audioStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(cleanMime);
            form.Add(fileContent, "file", string.IsNullOrWhiteSpace(fileName) ? "audio.ogg" : fileName);

            // Meta requiere estos campos en el upload
            form.Add(new StringContent("whatsapp"), "messaging_product");
            form.Add(new StringContent(cleanMime), "type");

            var uploadReq = new HttpRequestMessage(HttpMethod.Post, mediaUrl)
            {
                Content = form
            };
            uploadReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var uploadRes = await cli.SendAsync(uploadReq);
            var uploadBody = await uploadRes.Content.ReadAsStringAsync();

            if (!uploadRes.IsSuccessStatusCode)
            {
                return new DescriptiveBoolean
                {
                    Exitoso = false,
                    Mensaje = uploadBody,
                    StatusCode = (int)uploadRes.StatusCode
                };
            }

            string? mediaId = null;
            try
            {
                using var doc = JsonDocument.Parse(uploadBody);
                if (doc.RootElement.TryGetProperty("id", out var idEl) &&
                    idEl.ValueKind == JsonValueKind.String)
                {
                    mediaId = idEl.GetString();
                }
            }
            catch
            {
                // ignore, se maneja abajo
            }

            if (string.IsNullOrWhiteSpace(mediaId))
            {
                return new DescriptiveBoolean
                {
                    Exitoso = false,
                    Mensaje = "No se pudo obtener media id de WhatsApp.",
                    StatusCode = 500
                };
            }

            // =======================
            // 2) ENVIAR MENSAJE AUDIO
            // =======================
            var msgUrl = $"{baseUrl}/{version}/{phoneId}/messages";

            var payload = new
            {
                messaging_product = "whatsapp",
                to = toPhoneE164,
                type = "audio",
                audio = new
                {
                    id = mediaId
                }
            };

            var msgReq = new HttpRequestMessage(HttpMethod.Post, msgUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            msgReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var msgRes = await cli.SendAsync(msgReq);
            var msgBody = await msgRes.Content.ReadAsStringAsync();

            return new DescriptiveBoolean
            {
                Exitoso = msgRes.IsSuccessStatusCode,
                Mensaje = msgRes.IsSuccessStatusCode ? "OK" : msgBody,
                StatusCode = (int)msgRes.StatusCode
            };
        }
    }
}
