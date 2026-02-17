namespace Whatsapp_API.Models.Request.General
{
    public class IntegrationViewResponse
    {
        public int Id { get; set; }
        public string Provider { get; set; } = "";
        public string PhoneNumberId { get; set; } = "";
        public string? WabaId { get; set; }
        public string ApiBaseUrl { get; set; } = "";
        public string ApiVersion { get; set; } = "";
        public bool IsActive { get; set; }

        public string AccessTokenMasked { get; set; } = "";  // ****abcd
        public bool HasVerifyToken { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
