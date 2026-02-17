
namespace Whatsapp_API.Models.Request.General
{
    public class ConversationTouchRequest
    {
        public DateTime? Last_Activity_At { get; set; }
        public string? Status { get; set; } // por defecto "open"
    }
}
