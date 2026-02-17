using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Whatsapp_API.Models.Entities.System
{
    [Table("companies")]
    public class Company
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("name")]
        public string? Name { get; set; }

        public string? FlowKey { get; set; } //KEY PARA INTEGRACION CON FLOW DEL CHAT BOT, CON ESTO SE DETECTA QUE ALGUN CODIGO DE LA CARPETA BOTFLOWS ES DE ESTA EMPRESA
    }
}

