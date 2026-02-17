using System.ComponentModel.DataAnnotations;

namespace Whatsapp_API.Models.Request.DTO
{
    public class SetExpoTokenRequest
    {
        [Required]
        public int IdUser { get; set; }

        [Required, MaxLength(300)]
        public string ExpoToken { get; set; } = "";
    }
}
