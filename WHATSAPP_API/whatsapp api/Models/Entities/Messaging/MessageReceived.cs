using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Whatsapp_API.Models.Entities.Messaging
{
    [Table("received_messages")]
    public class MessageReceived
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("company_id")]
        public int CompanyId { get; set; }

        // id de mensaje reportado por Meta/Graph
        [Column("message_meta_id")]
        public string? MessageMetaId { get; set; }

        [Column("status")]
        public string? Status { get; set; }

        [Column("data_json")] 
        public string? DataJson { get; set; }
    }
}
