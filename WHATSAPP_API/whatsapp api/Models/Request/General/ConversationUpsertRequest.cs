using System.ComponentModel.DataAnnotations;

namespace Whatsapp_API.Models.Request.General
{
    public class ConversationUpsertRequest
    {
        public int Id { get; set; }

        [Required]
        public int Contact_Id { get; set; }

        public int? Agent_Id { get; set; }

        public DateTime? Started_At { get; set; }
        public DateTime? Last_Activity_At { get; set; }
        public DateTime? Ended_At { get; set; }

        [MaxLength(30)]
        public string? Status { get; set; }

        public bool? Greeting_Sent { get; set; }

        public int? Total_Messages { get; set; }
        public int? Ai_Messages { get; set; }

        public int? First_Response_Time { get; set; }
        public int? Rating { get; set; }
    }
}
