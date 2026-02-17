using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Whatsapp_API.Models.Entities.Messaging
{
    [Table("attachments")]
    public class Attachment
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        // EMPRESA (TENANT)
        [Column("company_id")]
        public int CompanyId { get; set; }

        // FK AL MENSAJE
        [Column("message_id")]
        public int MessageId { get; set; }

        // NOMBRE DEL ARCHIVO (LO QUE VAS A MOSTRAR EN EL FRONT)
        [Column("file_name")]
        [MaxLength(255)]
        public string FileName { get; set; } = "";

        // MIME TYPE (image/jpeg, audio/ogg, application/pdf, etc.)
        [Column("mime_type")]
        [MaxLength(255)]
        public string MimeType { get; set; } = "application/octet-stream";

        // TAMAÑO EN BYTES (OPCIONAL)
        [Column("size_bytes")]
        public long? SizeBytes { get; set; }

        // BINARIO DEL ARCHIVO (SI LO GUARDAS EN BD) – PUEDE SER NULL SI SOLO GUARDAS REFERENCIA
        [Column("data", TypeName = "varbinary(max)")]
        public byte[]? Data { get; set; }

        // PROVEEDOR DE ALMACENAMIENTO: "db", "whatsapp", "s3", ETC.
        [Column("storage_provider")]
        [MaxLength(50)]
        public string StorageProvider { get; set; } = "db";

        // RUTA O CLAVE EN EL STORAGE (EJ: MEDIA_ID DE WHATSAPP, RUTA EN S3, ETC.)
        [Column("storage_path")]
        [MaxLength(500)]
        public string? StoragePath { get; set; }

        // MEDIA ID DEVUELTO POR LA CLOUD API DE WHATSAPP
        [Column("whatsapp_media_id")]
        [MaxLength(255)]
        public string? WhatsappMediaId { get; set; }

        // FECHA EN QUE SE GUARDÓ EL ARCHIVO
        [Column("uploaded_at")]
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        // NAVIGATION PROPERTY (OPCIONAL, SI YA TIENES Message)
        public Message? Message { get; set; }
    }
}
