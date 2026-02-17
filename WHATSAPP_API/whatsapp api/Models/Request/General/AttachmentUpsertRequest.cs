using System.ComponentModel.DataAnnotations;

namespace Whatsapp_API.Models.Request.General
{
    public class AttachmentUpsertRequest
    {
        public int Id { get; set; } 

        [Required]
        public int Message_Id { get; set; }

        [Required, MaxLength(260)]
        public string File_Name { get; set; } = "";

        [MaxLength(100)]
        public string? Mime_Type { get; set; }

        // Contenido en Base64 
        public string? Data_Base64 { get; set; }

        public DateTime? Uploaded_At { get; set; }
    }
}
