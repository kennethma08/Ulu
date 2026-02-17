using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Whatsapp_API.Models.Entities.Security
{
    [Table("users")]
    public class User
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("name")]
        public string? Name { get; set; }

        [Column("email")]
        public string? Email { get; set; }

        [Column("pass")]
        public string? Pass { get; set; }

        [Column("phone")]
        public string? Phone { get; set; }

        [Column("status")]
        public bool? Status { get; set; }

        [Column("idProfile")]
        public int? IdProfile { get; set; }

        [Column("company")]
        public string? Company { get; set; }

        [Column("company_id")]
        public int? CompanyId { get; set; }

        [Column("contact_id")]
        public int? ContactId { get; set; }

        [Column("last_login")]
        public DateTime? LastLogin { get; set; }

        [Column("last_activity")]
        public DateTime? LastActivity { get; set; }

        [Column("is_online")]
        public bool IsOnline { get; set; }

        [Column("avatar_mime_type")]
        public string? AvatarMimeType { get; set; }

        [Column("avatar_file_name")]
        public string? AvatarFileName { get; set; }

        [Column("avatar_updated_at")]
        public DateTime? AvatarUpdatedAt { get; set; }

        [Column("conversation_count")]
        public int ConversationCount { get; set; }
    }
}
