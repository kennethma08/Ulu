using System.ComponentModel.DataAnnotations;

namespace Whatsapp_API.Models.Request.User
{
    public class UserDeleteRequest
    {
        [Required]
        [Range(1, short.MaxValue, ErrorMessage = "El RolID debe ser mayor a 0.")]
        public int Id { get; set; }
    }
}
