namespace Whatsapp_API.Models.Request.General
{
    public class ConversationAssignRequest
    {
        // si viene null = soltar (quitar asignación)
        public int? ToUserId { get; set; }

        public string? Reason { get; set; }
    }
}
