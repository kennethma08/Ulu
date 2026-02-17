using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Whatsapp_API.Models.Entities.System
{
    [Table("integrations")]
    public class Integration
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("provider")]
        public string Provider { get; set; } = "whatsapp_cloud"; //SIEMPRE WHATSAPP_CLOUD, SI NO ES ASI, NO FUNCIONA. *PARA FUTURAS INTEGRACIONES CON OTRAS PLATAFORMAS, HABRÁ QUE CAMBIAR ESTO*

        [Column("phone_number_id")]
        public string PhoneNumberId { get; set; } = "";

        [Column("waba_id")] ////*REVISAR PARA ELIMINAR O SI HACE FALTA*
        public string? WabaId { get; set; }

        [Column("access_token_enc", TypeName = "varbinary(max)")]
        public byte[]? AccessTokenEnc { get; set; }

        [Column("verify_token_hash")]
        public string? VerifyTokenHash { get; set; }

        [Column("api_base_url")]
        public string ApiBaseUrl { get; set; } = "https://graph.facebook.com"; //URL BASE DE LA API DE WHATSAPP CLOUD, PONER SIEMPRE ESTA

        [Column("api_version")] //VERSION DE LA API DE WHATSAPP CLOUD, ACTUALIZAR CUANDO META SAQUE UNA NUEVA VERSION O USAR PASADAS SI FALLA LA ACTUAL (ACTUAL V22.0)
        public string ApiVersion { get; set; } = "v20.0";

        [Column("is_active")]
        public bool IsActive { get; set; } = true; 

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        [Column("company_id")]
        public int CompanyId { get; set; }
    }
}
