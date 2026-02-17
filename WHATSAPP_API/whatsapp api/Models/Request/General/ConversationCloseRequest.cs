namespace Whatsapp_API.Models.Request.General
{
    public class ConversationCloseRequest
    {
        public DateTime? Ended_At { get; set; }
        public string? Reason { get; set; }
    }
}
