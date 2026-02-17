using System.ComponentModel.DataAnnotations;

namespace Whatsapp_API.Models.Request.General
{
    public class ConversationHoldRequest
    {
        [MaxLength(500)]
        public string? Reason { get; set; }
    }
}
