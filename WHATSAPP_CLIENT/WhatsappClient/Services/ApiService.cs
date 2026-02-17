using System;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using WhatsappClient.Models;

namespace WhatsappClient.Services
{
    public class ApiService
    {
        private readonly HttpClient _http;
        private readonly string _companyIdFallback;
        private readonly IHttpContextAccessor _accessor;

        private const string HDR_COMPANY = "X-Company-Id";

        private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        public ApiService(HttpClient http, string companyId, IHttpContextAccessor accessor)
        {
            _http = http;
            _companyIdFallback = string.IsNullOrWhiteSpace(companyId) ? "1" : companyId.Trim();
            _accessor = accessor;
        }

        private string CurrentEmpresaId()
        {
            var emp = _accessor.HttpContext?.Session?.GetString("COMPANY_ID");
            if (string.IsNullOrWhiteSpace(emp)) emp = _companyIdFallback;
            if (string.IsNullOrWhiteSpace(emp)) emp = "1";
            return emp!;
        }

        private static string CleanupToken(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return string.Empty;
            var s = t.Trim();
            if (s.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) s = s.Substring(7).Trim();
            if (s.StartsWith("\"") && s.EndsWith("\"")) s = s.Trim('\"');
            return s;
        }

        private static DateTimeOffset? GetJwtExpiry(string token)
        {
            try
            {
                var parts = token.Split('.');
                if (parts.Length < 2) return null;

                string Base64UrlToBase64(string i)
                {
                    i = i.Replace('-', '+').Replace('_', '/');
                    return i.PadRight(i.Length + (4 - i.Length % 4) % 4, '=');
                }

                var payloadJson = Encoding.UTF8.GetString(
                    Convert.FromBase64String(Base64UrlToBase64(parts[1]))
                );

                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("exp", out var expEl))
                {
                    long exp = expEl.ValueKind == JsonValueKind.String
                        ? long.Parse(expEl.GetString()!)
                        : expEl.GetInt64();

                    return DateTimeOffset.FromUnixTimeSeconds(exp);
                }
            }
            catch { }

            return null;
        }

        private static int? GetUserIdFromJwt(string token)
        {
            try
            {
                var parts = token.Split('.');
                if (parts.Length < 2) return null;

                string Base64UrlToBase64(string i)
                {
                    i = i.Replace('-', '+').Replace('_', '/');
                    return i.PadRight(i.Length + (4 - i.Length % 4) % 4, '=');
                }

                var payloadJson = Encoding.UTF8.GetString(
                    Convert.FromBase64String(Base64UrlToBase64(parts[1]))
                );

                using var doc = JsonDocument.Parse(payloadJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("sub", out var subEl))
                {
                    if (subEl.ValueKind == JsonValueKind.String && int.TryParse(subEl.GetString(), out var id))
                        return id;
                    if (subEl.ValueKind == JsonValueKind.Number && subEl.TryGetInt32(out var idNum))
                        return idNum;
                }

                if (root.TryGetProperty("nameid", out var nameIdEl))
                {
                    if (nameIdEl.ValueKind == JsonValueKind.String && int.TryParse(nameIdEl.GetString(), out var id2))
                        return id2;
                    if (nameIdEl.ValueKind == JsonValueKind.Number && nameIdEl.TryGetInt32(out var idNum2))
                        return idNum2;
                }
            }
            catch { }

            return null;
        }

        private static bool IsExpiredOrNear(DateTimeOffset? exp, int skewSeconds = 60)
        {
            if (exp == null) return false;
            return DateTimeOffset.UtcNow >= exp.Value.AddSeconds(-skewSeconds);
        }

        private void ApplyHeaders()
        {
            if (_http.DefaultRequestHeaders.Contains(HDR_COMPANY))
                _http.DefaultRequestHeaders.Remove(HDR_COMPANY);
            if (_http.DefaultRequestHeaders.Contains("X-Empresa-Id"))
                _http.DefaultRequestHeaders.Remove("X-Empresa-Id");
            if (_http.DefaultRequestHeaders.Contains("X-Empresa"))
                _http.DefaultRequestHeaders.Remove("X-Empresa");

            var company = CurrentEmpresaId();

            _http.DefaultRequestHeaders.Add(HDR_COMPANY, company);
            _http.DefaultRequestHeaders.Add("X-Empresa-Id", company);
            _http.DefaultRequestHeaders.Add("X-Empresa", company);

            var raw = _accessor.HttpContext?.Session?.GetString("JWT_TOKEN")
                      ?? _accessor.HttpContext?.User?.FindFirst("jwt")?.Value;

            var token = CleanupToken(raw ?? string.Empty);
            var exp = GetJwtExpiry(token);
            if (IsExpiredOrNear(exp))
            {
                token = string.Empty;
                try { _accessor.HttpContext?.Session?.Remove("JWT_TOKEN"); } catch { }
            }

            _http.DefaultRequestHeaders.Authorization = null;
            if (!string.IsNullOrWhiteSpace(token))
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            _http.DefaultRequestHeaders.Accept.Clear();
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private async Task<HttpResponseMessage> GetWithRetryAsync(string url)
        {
            ApplyHeaders();
            var resp = await _http.GetAsync(url);

            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _http.DefaultRequestHeaders.Authorization = null;
                ApplyHeaders();
                resp = await _http.GetAsync(url);
            }

            return resp;
        }

        private async Task<HttpResponseMessage> PostWithRetryAsync(string url, HttpContent content)
        {
            ApplyHeaders();
            var resp = await _http.PostAsync(url, content);

            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _http.DefaultRequestHeaders.Authorization = null;
                ApplyHeaders();
                resp = await _http.PostAsync(url, content);
            }

            return resp;
        }

        private async Task<List<T>> GetListAsync<T>(string url)
        {
            var resp = await GetWithRetryAsync(url);
            if (!resp.IsSuccessStatusCode) return new();
            var body = await resp.Content.ReadAsStringAsync();
            return DeserializeFlexibleList<T>(body);
        }

        public Task<List<ContactDto>> ObtenerContactosAsync()
            => GetListAsync<ContactDto>("api/general/contact");

        public Task<List<ConversationSessionDto>> ObtenerConversacionesAsync()
            => GetListAsync<ConversationSessionDto>("api/general/conversation");

        public Task<List<MessageDto>> ObtenerMensajesAsync()
            => GetListAsync<MessageDto>("api/general/message");

        public async Task<List<ConversationPanelItem>> ObtenerConversacionesPanelAsync()
        {
            var resp = await GetWithRetryAsync("api/general/conversation/panel");
            if (!resp.IsSuccessStatusCode) return new List<ConversationPanelItem>();

            var body = await resp.Content.ReadAsStringAsync();
            try
            {
                using var doc = JsonDocument.Parse(body);
                var items = ExtraerItems(doc.RootElement);

                var list = new List<ConversationPanelItem>();
                foreach (var e in items)
                {
                    list.Add(new ConversationPanelItem
                    {
                        Id = GetIntFlex(e, "id", "Id") ?? 0,
                        ContactId = GetIntFlex(e, "contact_id", "ContactId", "contactId"),
                        Status = GetStringFlex(e, "status", "Status") ?? "open",
                        StartedAt = GetDateFlex(e, "started_at", "StartedAt", "startedAt"),
                        LastActivityAt = GetDateFlex(e, "last_activity_at", "LastActivityAt", "lastActivityAt"),
                        AgentRequestedAt = GetDateFlex(e, "agent_requested_at", "AgentRequestedAt", "agentRequestedAt"),
                        AssignedUserId = GetIntFlex(e, "assigned_user_id", "AssignedUserId", "assignedUserId"),
                        IsOnHold = GetBoolFlex(e, "is_on_hold", "IsOnHold") ?? false,
                        OnHoldReason = GetStringFlex(e, "on_hold_reason", "OnHoldReason")
                    });
                }
                return list.Where(x => x.Id > 0).ToList();
            }
            catch
            {
                return new List<ConversationPanelItem>();
            }
        }

        public async Task<(bool ok, string? error)> AssignConversationAsync(int conversationId, int toUserId)
        {
            if (conversationId <= 0 || toUserId <= 0) return (false, "Parámetros inválidos.");

            var payload = JsonSerializer.Serialize(new { toUserId });
            var resp = await PostWithRetryAsync($"api/general/conversation/{conversationId}/assign",
                new StringContent(payload, Encoding.UTF8, "application/json"));

            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return (false, body);
            return (true, null);
        }

        public async Task<(bool ok, string? error)> ReleaseConversationAsync(int conversationId)
        {
            if (conversationId <= 0) return (false, "Parámetros inválidos.");

            var resp = await PostWithRetryAsync($"api/general/conversation/{conversationId}/release",
                new StringContent("{}", Encoding.UTF8, "application/json"));

            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return (false, body);
            return (true, null);
        }

        public async Task<(bool ok, string? error)> UpsertConversationStatusAsync(int conversationId, int? contactId, DateTime? startedAt, string status)
        {
            if (conversationId <= 0) return (false, "conversationId inválido.");
            if (string.IsNullOrWhiteSpace(status)) return (false, "status inválido.");

            var payload = new
            {
                Id = conversationId,
                Contact_Id = contactId,
                Started_At = startedAt ?? DateTime.UtcNow,
                Status = status.Trim(),
                Last_Activity_At = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = null });

            var resp = await PostWithRetryAsync("api/general/conversation/upsert",
                new StringContent(json, Encoding.UTF8, "application/json"));

            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return (false, body);
            return (true, null);
        }

        public async Task<List<UserDto>> GetUsuariosAsync()
        {
            var resp = await GetWithRetryAsync("api/seguridad/user");
            if (!resp.IsSuccessStatusCode) return new List<UserDto>();

            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var items = ExtraerItems(doc.RootElement);
            var list = new List<UserDto>();

            foreach (var e in items)
            {
                var u = new UserDto
                {
                    Id = GetIntFlex(e, "id", "Id", "usuarioId", "userId") ?? 0,
                    Name = GetStringFlex(e, "nombre", "Nombre", "name", "Name", "nombreUsuario", "usuario"),
                    Email = GetStringFlex(e, "correo", "Correo", "email", "Email"),
                    idNumber = GetStringFlex(e, "idNumber", "IDNumber", "dni"),
                    Phone = GetStringFlex(e, "telefono", "Telefono", "phone", "Phone"),
                    Status = GetBoolFlex(e, "status", "Status", "activo", "isActive"),
                    IdProfile = GetIntFlex(e, "idProfile", "IdProfile", "profileId", "ProfileId"),
                    Company = GetStringFlex(e, "company", "Company"),
                    AgentId = GetIntFlex(e, "agentId", "AgentId"),
                    ContactId = GetIntFlex(e, "contactId", "ContactId"),
                    LastLogin = GetDateFlex(e, "lastLogin", "LastLogin", "ultimoAcceso", "UltimoAcceso"),
                    LastActivity = GetDateFlex(e, "lastActivity", "LastActivity", "ultimoMovimiento", "UltimoMovimiento"),
                    IsOnline = GetBoolFlex(e, "isOnline", "online", "Online", "conectado", "Conectado") ?? false,
                    ConversationCount = GetIntFlex(e, "conversationCount", "ConversationCount", "totalConversaciones") ?? 0
                };

                var empId = GetIntFlex(e, "companyId", "CompanyId", "companyID", "CompanyID");
                if (empId.HasValue) u.CompanyID = empId.Value;

                if (u.Id != 0 || !string.IsNullOrEmpty(u.Email) || !string.IsNullOrEmpty(u.Name))
                    list.Add(u);
            }

            return list;
        }

        public async Task<UserDto?> GetUsuarioByIdAsync(int id)
        {
            if (id <= 0) return null;

            var resp = await GetWithRetryAsync($"api/seguridad/user/{id}");
            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadAsStringAsync();
            var u = DeserializeFlexibleSingle<UserDto>(body);

            if (u == null)
            {
                using var doc = JsonDocument.Parse(body);
                var item = ExtraerItems(doc.RootElement).FirstOrDefault();
                if (item.ValueKind != JsonValueKind.Undefined)
                    u = MapUsuario(item);
            }

            return u;
        }

        public async Task<List<UserDto>> GetAgentesAsync()
        {
            var resp = await GetWithRetryAsync("api/seguridad/user/by-perfil-id/1");
            if (!resp.IsSuccessStatusCode)
            {
                var all = await GetUsuariosAsync();
                return all.Where(u => (u.IdProfile ?? 0) == 1).ToList();
            }

            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var items = ExtraerItems(doc.RootElement);
            var list = new List<UserDto>();

            foreach (var e in items)
            {
                var u = MapUsuario(e);
                if (u != null) list.Add(u);
            }

            return list;
        }

        public async Task<ApiResponse<T>?> GetAsync<T>(string url)
        {
            var resp = await GetWithRetryAsync(url);
            if (!resp.IsSuccessStatusCode) return null;

            try
            {
                return await resp.Content.ReadFromJsonAsync<ApiResponse<T>>(_json);
            }
            catch
            {
                return null;
            }
        }

        public async Task<string> GetRawAsync(string url)
        {
            var resp = await GetWithRetryAsync(url);
            var body = await resp.Content.ReadAsStringAsync();
            return $"[{(int)resp.StatusCode}] {url}\n{body}";
        }

        public async Task<bool> UpdateNombreAgenteAsync(int idUser, string name)
        {
            HttpRequestMessage BuildReq() => new HttpRequestMessage(
                HttpMethod.Patch,
                $"api/seguridad/user/{idUser}/nombre"
            )
            {
                Content = JsonContent.Create(new { Nombre = name })
            };

            ApplyHeaders();

            using var req = BuildReq();
            using var resp = await _http.SendAsync(req);

            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _http.DefaultRequestHeaders.Authorization = null;
                ApplyHeaders();

                using var req2 = BuildReq();
                using var resp2 = await _http.SendAsync(req2);
                return resp2.IsSuccessStatusCode;
            }

            return resp.IsSuccessStatusCode;
        }

        public async Task<bool> UpdateNombreContactoAsync(int idContact, string name)
        {
            HttpRequestMessage BuildReq() => new HttpRequestMessage(
                HttpMethod.Patch,
                $"api/general/contact/{idContact}/nombre"
            )
            {
                Content = JsonContent.Create(new { Name = name })
            };

            ApplyHeaders();

            using var req = BuildReq();
            using var resp = await _http.SendAsync(req);

            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _http.DefaultRequestHeaders.Authorization = null;
                ApplyHeaders();
                using var req2 = BuildReq();
                using var resp2 = await _http.SendAsync(req2);
                return resp2.IsSuccessStatusCode;
            }

            return resp.IsSuccessStatusCode;
        }

        public async Task<bool> UpdatePerfilUsuarioAsync(int idUser, string name, string email, string company)
        {
            if (idUser <= 0)
            {
                var raw = _accessor.HttpContext?.Session?.GetString("JWT_TOKEN")
                          ?? _accessor.HttpContext?.User?.FindFirst("jwt")?.Value;
                var token = CleanupToken(raw ?? string.Empty);
                var fromJwt = GetUserIdFromJwt(token);
                if (fromJwt.HasValue) idUser = fromJwt.Value;
            }

            if (idUser <= 0) return false;

            ApplyHeaders();

            var payload = new
            {
                Id = idUser,
                Name = name,
                Email = email,
                Company = company
            };

            var resp = await _http.PostAsJsonAsync("api/seguridad/user/upsert", payload);
            if (!resp.IsSuccessStatusCode) return false;

            var body = await resp.Content.ReadAsStringAsync();
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var ok = GetBoolFlex(root, "exitoso", "success") ?? true;
                return ok;
            }
            catch
            {
                return true;
            }
        }

        public async Task<(bool ok, string? error)> ChangePasswordAsync(int idUser, string currentPassword, string newPassword)
        {
            if (idUser <= 0)
            {
                var raw = _accessor.HttpContext?.Session?.GetString("JWT_TOKEN")
                          ?? _accessor.HttpContext?.User?.FindFirst("jwt")?.Value;
                var token = CleanupToken(raw ?? string.Empty);
                var fromJwt = GetUserIdFromJwt(token);
                if (fromJwt.HasValue) idUser = fromJwt.Value;
            }

            if (idUser <= 0) return (false, "Id de usuario inválido.");

            ApplyHeaders();

            var payload = new
            {
                currentPassword,
                newPassword
            };

            var resp = await _http.PostAsJsonAsync($"api/seguridad/user/{idUser}/change-password", payload);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    var msg = GetStringFlex(root, "mensaje", "message", "error") ?? body;
                    return (false, msg);
                }
                catch
                {
                    return (false, body);
                }
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var ok = GetBoolFlex(root, "exitoso", "success") ?? resp.IsSuccessStatusCode;
                if (!ok)
                {
                    var msg = GetStringFlex(root, "mensaje", "message", "error") ?? body;
                    return (false, msg);
                }
            }
            catch { }

            return (true, null);
        }

        private static List<T> DeserializeFlexibleList<T>(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                    return JsonSerializer.Deserialize<List<T>>(root.GetRawText(), _json) ?? new();

                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("$values", out var rValues) && rValues.ValueKind == JsonValueKind.Array)
                        return JsonSerializer.Deserialize<List<T>>(rValues.GetRawText(), _json) ?? new();

                    if (root.TryGetProperty("data", out var data))
                    {
                        if (data.ValueKind == JsonValueKind.Array)
                            return JsonSerializer.Deserialize<List<T>>(data.GetRawText(), _json) ?? new();

                        if (data.ValueKind == JsonValueKind.Object)
                        {
                            if (data.TryGetProperty("$values", out var dValues) && dValues.ValueKind == JsonValueKind.Array)
                                return JsonSerializer.Deserialize<List<T>>(dValues.GetRawText(), _json) ?? new();

                            foreach (var key in new[] { "values", "Values", "items", "Items", "list", "List" })
                                if (data.TryGetProperty(key, out var node) && node.ValueKind == JsonValueKind.Array)
                                    return JsonSerializer.Deserialize<List<T>>(node.GetRawText(), _json) ?? new();
                        }
                    }

                    foreach (var key in new[] { "values", "Values", "items", "Items", "list", "List" })
                        if (root.TryGetProperty(key, out var node) && node.ValueKind == JsonValueKind.Array)
                            return JsonSerializer.Deserialize<List<T>>(node.GetRawText(), _json) ?? new();
                }
            }
            catch { }

            return new();
        }

        private static T? DeserializeFlexibleSingle<T>(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                        return JsonSerializer.Deserialize<T>(data.GetRawText(), _json);

                    if (root.TryGetProperty("object", out var obj) && obj.ValueKind == JsonValueKind.Object)
                        return JsonSerializer.Deserialize<T>(obj.GetRawText(), _json);

                    return JsonSerializer.Deserialize<T>(root.GetRawText(), _json);
                }
            }
            catch { }

            return default;
        }

        private static IEnumerable<JsonElement> ExtraerItems(JsonElement root)
        {
            if (TryGetCaseInsensitive(root, "data", out var data))
            {
                if (TryGetCaseInsensitive(data, "$values", out var values) && values.ValueKind == JsonValueKind.Array)
                    return values.EnumerateArray().ToArray();

                if (data.ValueKind == JsonValueKind.Array)
                    return data.EnumerateArray().ToArray();
            }

            if (TryGetCaseInsensitive(root, "$values", out var rvalues) && rvalues.ValueKind == JsonValueKind.Array)
                return rvalues.EnumerateArray().ToArray();

            if (root.ValueKind == JsonValueKind.Array)
                return root.EnumerateArray().ToArray();

            foreach (var p in root.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Array)
                    return p.Value.EnumerateArray().ToArray();

            return Array.Empty<JsonElement>();
        }

        private static bool TryGetCaseInsensitive(JsonElement obj, string name, out JsonElement value)
        {
            if (obj.ValueKind != JsonValueKind.Object)
            {
                value = default;
                return false;
            }

            foreach (var p in obj.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static string? GetStringFlex(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
                if (TryGetCaseInsensitive(obj, n, out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString();

            return null;
        }

        private static int? GetIntFlex(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
            {
                if (TryGetCaseInsensitive(obj, n, out var v))
                {
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
                    if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out i)) return i;
                }
            }

            return null;
        }

        private static bool? GetBoolFlex(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
            {
                if (TryGetCaseInsensitive(obj, n, out var v))
                {
                    if (v.ValueKind == JsonValueKind.True) return true;
                    if (v.ValueKind == JsonValueKind.False) return false;
                    if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b)) return b;
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i != 0;
                }
            }

            return null;
        }

        private static DateTime? GetDateFlex(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
            {
                if (TryGetCaseInsensitive(obj, n, out var v))
                {
                    if (v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(), out var dt))
                        return dt;

                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var epoch))
                        return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
                }
            }

            return null;
        }

        private static UserDto? MapUsuario(JsonElement e)
        {
            var u = new UserDto
            {
                Id = GetIntFlex(e, "id", "Id", "usuarioId", "userId") ?? 0,
                Name = GetStringFlex(e, "nombre", "Nombre", "name", "Name", "nombreUsuario", "usuario"),
                Email = GetStringFlex(e, "correo", "Correo", "email", "Email"),
                idNumber = GetStringFlex(e, "idNumber", "IDNumber", "dni"),
                Phone = GetStringFlex(e, "telefono", "Telefono", "phone", "Phone"),
                Status = GetBoolFlex(e, "status", "Status", "activo", "isActive"),
                IdProfile = GetIntFlex(e, "idProfile", "IdProfile", "profileId", "ProfileId"),
                Company = GetStringFlex(e, "company", "Company"),
                AgentId = GetIntFlex(e, "agentId", "AgentId"),
                ContactId = GetIntFlex(e, "contactId", "ContactId"),
                LastLogin = GetDateFlex(e, "lastLogin", "LastLogin", "ultimoAcceso", "UltimoAcceso"),
                LastActivity = GetDateFlex(e, "lastActivity", "LastActivity", "ultimoMovimiento", "UltimoMovimiento"),
                IsOnline = GetBoolFlex(e, "isOnline", "online", "Online", "conectado", "Conectado") ?? false,
                ConversationCount = GetIntFlex(e, "conversationCount", "ConversationCount", "totalConversaciones") ?? 0
            };

            var empId = GetIntFlex(e, "companyId", "CompanyId", "companyID", "CompanyID");
            if (empId.HasValue) u.CompanyID = empId.Value;

            if (u.Id == 0 && string.IsNullOrWhiteSpace(u.Email) && string.IsNullOrWhiteSpace(u.Name))
                return null;

            return u;
        }

        public class ConversationPanelItem
        {
            public int Id { get; set; }
            public int? ContactId { get; set; }
            public string Status { get; set; } = "open";
            public DateTime? StartedAt { get; set; }
            public DateTime? LastActivityAt { get; set; }
            public DateTime? AgentRequestedAt { get; set; }
            public int? AssignedUserId { get; set; }

            // NUEVO
            public bool IsOnHold { get; set; }
            public string? OnHoldReason { get; set; }
        }
    }
}