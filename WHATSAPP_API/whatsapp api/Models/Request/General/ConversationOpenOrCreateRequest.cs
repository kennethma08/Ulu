namespace Whatsapp_API.Models.Request.General
{
    public class ConversationOpenOrCreateRequest
    {
        public int Contact_Id { get; set; }
        public DateTime? Started_At { get; set; }
        public bool? Fresh_Only { get; set; } = true; 
    }
}
