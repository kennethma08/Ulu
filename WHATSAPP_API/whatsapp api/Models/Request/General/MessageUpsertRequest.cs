using System.ComponentModel.DataAnnotations;

namespace Whatsapp_API.Models.Request.General
{
    public class MessageUpsertRequest
    {
        public int Id { get; set; } 

        [Required]
        public int Conversation_Id { get; set; }

        [Required]
        public int Contact_Id { get; set; }

        public int? Agent_Id { get; set; }

        [Required, MaxLength(30)]
        public string Sender { get; set; } = "contact"; 

        public string? Message { get; set; }

        [Required, MaxLength(30)]
        public string Type { get; set; } = "text"; 

        public DateTime? Sent_At { get; set; }

        // Campos para ubicación PROBAR SI SIRVE Y LO DEVUELVE LA API DE WHATSAPP
        public decimal? Latitude { get; set; }   
        public decimal? Longitude { get; set; }  
        public string? Location_Name { get; set; }
    }
}
