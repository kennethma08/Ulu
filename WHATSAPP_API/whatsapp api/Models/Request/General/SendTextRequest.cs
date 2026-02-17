using System.ComponentModel.DataAnnotations;

namespace Whatsapp_API.Models.Request.General
{
    public class SendTextRequest
    {
        public string? To_Phone { get; set; }

        public int? Contact_Id { get; set; }

        public int? Agent_Id { get; set; }

        public int? Conversation_Id { get; set; }

        public bool Create_If_Not_Exists { get; set; } = true;

        [Required]
        public string Text { get; set; } = "";

        public bool Log { get; set; } = true;
    }
}
