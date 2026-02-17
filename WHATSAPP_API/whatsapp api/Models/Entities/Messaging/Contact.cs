using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Whatsapp_API.Models.Entities.Messaging
{
    [Table("contacts")]
    public class Contact
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("name")]
        public string? Name { get; set; }

        [Column("phone_number")]
        public string? PhoneNumber { get; set; }


        [Column("country")] // *REVISAR PARA ELIMINAR O NO, REVISAR SI META PUEDE OBTENER EL PAIS*
        public string? Country { get; set; }


        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }

        [Column("last_message_at")]
        public DateTime? LastMessageAt { get; set; }

        [Column("status")]
        public string? Status { get; set; }

        [Column("welcome_sent")]
        public bool WelcomeSent { get; set; }

        [Column("company_id")]
        public int CompanyId { get; set; }

        [Column("last_location_latitude")]
        public double? LastLocationLatitude { get; set; }  // Almacena latitud

        [Column("last_location_longitude")]
        public double? LastLocationLongitude { get; set; } // Almacena longitud
    }
}
