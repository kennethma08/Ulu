using System.ComponentModel.DataAnnotations;

namespace Whatsapp_API.Models.Request.User
{
    public class UserCreateUpdateRequest
    {
        public int Id { get; set; } 

        [Required, MaxLength(150)]
        public string Name { get; set; } = "";

        [Required, EmailAddress, MaxLength(200)]
        public string Email { get; set; } = "";

        [MaxLength(50)]
        public string? IDNumber { get; set; }

        [MaxLength(200)]
        public string? Pass { get; set; }

        [MaxLength(30)]
        public string? Phone { get; set; }

        public bool? Status { get; set; }

        public int? IdProfile { get; set; }
        public string? Company { get; set; }
        public int? AgentId { get; set; }
        public int? ContactId { get; set; }
        public bool? IsOnline { get; set; }
        public int? ConversationCount { get; set; }
    }
}
