namespace WhatsappClient.Models
{
    public class UserDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? idNumber { get; set; }
        public string? Phone { get; set; }
        public bool? Status { get; set; }
        public int? IdProfile { get; set; }
        public string? Company { get; set; }

        public int? AgentId { get; set; }
        public int? CompanyID { get; set; }
        public int? ContactId { get; set; }
        public DateTime? LastLogin { get; set; }
        public DateTime? LastActivity { get; set; }
        public bool IsOnline { get; set; }
        public int ConversationCount { get; set; }
    }
}
