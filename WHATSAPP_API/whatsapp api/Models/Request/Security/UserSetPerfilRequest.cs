using System.ComponentModel.DataAnnotations;

namespace Whatsapp_API.Models.Request.Security
{
    public class UserSetPerfilRequest
    {
        [Required]
        public int IdProfile { get; set; }
    }
}
