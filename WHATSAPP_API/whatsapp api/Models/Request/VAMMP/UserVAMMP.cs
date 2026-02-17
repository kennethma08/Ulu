using System.Text.Json.Serialization;

namespace Whatsapp_API.Models.Request.VAMMP
{
    /// <summary>
    /// Modelo para recibir datos de usuario desde VAMMP (DTO de entrada).
    /// SOLO empresa_id (no company_id).
    /// </summary>
    public class UserVAMMP
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("email")] public string? Email { get; set; }
        [JsonPropertyName("idNumber")] public string? IDNumber { get; set; }
        [JsonPropertyName("pass")] public string? Pass { get; set; }
        [JsonPropertyName("phone")] public string? Phone { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("idProfile")] public int? IdProfile { get; set; }
        [JsonPropertyName("company")] public string? Company { get; set; }
        [JsonPropertyName("agent_id")] public int? AgentId { get; set; }

        [JsonPropertyName("company_id")] public int? CompanyId { get; set; }

        [JsonPropertyName("contact_id")] public int? ContactId { get; set; }

        [JsonPropertyName("last_login")] public DateTime? LastLogin { get; set; }
        [JsonPropertyName("last_activity")] public DateTime? LastActivity { get; set; }
        [JsonPropertyName("is_online")] public bool? IsOnline { get; set; }
        [JsonPropertyName("conversation_count")] public int? ConversationCount { get; set; }
    }
}
