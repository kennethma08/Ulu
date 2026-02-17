using System.ComponentModel.DataAnnotations;

namespace Whatsapp_API.Models.Request.Security
{
    public class ProfileUpsertRequest
    {
        public int Id { get; set; } 

        [Required]
        [MaxLength(100)]
        public string? Name { get; set; }
    }
}
