using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Whatsapp_API.Models.Entities.Security
{
    [Table("profiles")]
    public class Profile
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("name")]
        public string? Name { get; set; }

        [Column("company_id")]
        public int CompanyId { get; set; }
    }
}
