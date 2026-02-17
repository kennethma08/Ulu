using System.ComponentModel.DataAnnotations;

namespace Whatsapp_API.Models.Request.General
{
    public class ContactUpsertRequest
    {
        public int Id { get; set; } 

        [Required, MaxLength(200)]
        public string Name { get; set; } = "";

        [MaxLength(30)]
        public string? Phone_Number { get; set; }

        [MaxLength(100)]
        public string? Country { get; set; }

        [MaxLength(100)]
        public string? Ip_Address { get; set; }

        public DateTime? Created_At { get; set; }       
        public DateTime? Last_Message_At { get; set; }

        [MaxLength(500)]
        public string? Profile_Pic { get; set; }

        [MaxLength(50)]
        public string? Status { get; set; }

        public bool? Welcome_Sent { get; set; }
    }
}
