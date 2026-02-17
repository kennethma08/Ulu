using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Whatsapp_API.Models.Entities.Messaging
{
    [Table("messages")]
    public class Message
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("conversation_id")]
        public int ConversationId { get; set; }

        [Column("contact_id")]
        public int ContactId { get; set; }

        [Column("sender")]
        public string Sender { get; set; } = "contact";

        [Column("message")]
        public string? Messages { get; set; }

        [Column("type")]
        public string Type { get; set; } = "text"; 

        [Column("sent_at")]
        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        [Column("latitude", TypeName = "decimal(10,6)")]
        public decimal? Latitude { get; set; }

        [Column("longitude", TypeName = "decimal(10,6)")]
        public decimal? Longitude { get; set; }

        [Column("location_name")] 
        public string? LocationName { get; set; }

        [Column("company_id")]
        public int CompanyId { get; set; }


        public Conversation Conversation { get; set; } = null!;
        public Contact Contact { get; set; } = null!;
        public List<Attachment> Attachments { get; set; } = new();
    }
}
