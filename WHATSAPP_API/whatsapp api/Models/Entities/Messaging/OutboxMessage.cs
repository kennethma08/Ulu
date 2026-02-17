using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Whatsapp_API.Models.Entities.Messaging
{
    // mensajes en cola para salir
    [Table("outbox_messages")]
    public class OutboxMessage
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("company_id")]
        public int CompanyId { get; set; } // dueño del envío

        [Column("integration_id")]
        public int IntegrationId { get; set; } // con qué integración se manda

        [Column("phone_number_id")]
        public string? PhoneNumberId { get; set; } // línea desde donde sale

        [Column("recepient")]
        public string? Recepient { get; set; } // a quién va

        [Column("type")]
        public string? Type { get; set; } // texto, imagen, documento, etc.

        [Column("content_json")]
        public string? ContentJson { get; set; } // el payload del mensaje

        [Column("status")]
        public string? Status { get; set; } // pendiente, enviado, fallido

        [Column("tries")]
        public int Tries { get; set; } // cuántas veces se intentó

        [Column("next_attempt_at")]
        public DateTime? NextAttemptAt { get; set; } // cuándo reintentar

        [Column("message_meta_id")]
        public string? MessageMetaId { get; set; } // id que devuelve meta



        //  *REVISAR CORRECTO FUNCIONAMIENTO*

        [Column("error_code")]
        public string? ErrorCode { get; set; } // código del fallo si hubo

        [Column("error_message")]
        public string? ErrorMessage { get; set; } // detalle del fallo
    }
}
