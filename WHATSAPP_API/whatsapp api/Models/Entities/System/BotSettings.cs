using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Whatsapp_API.Models.Entities.System
{
    [Table("bot_settings")] //REVISAR ENTIDAD
    public class BotSettings
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("company_id")]
        public int CompanyId { get; set; }

        [Column("name")]
        public string? Name { get; set; }

        [Column("configuration_json")]
        public string? ConfigurationJson { get; set; }
    }
}
