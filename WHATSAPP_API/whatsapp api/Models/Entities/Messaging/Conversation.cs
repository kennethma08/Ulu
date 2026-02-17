using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Whatsapp_API.Models.Entities.Security;

namespace Whatsapp_API.Models.Entities.Messaging
{
    [Table("conversations")]
    public class Conversation
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("contact_id")]
        public int ContactId { get; set; }

        [Column("started_at")]
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        [Column("last_activity_at")]
        public DateTime? LastActivityAt { get; set; }

        [Column("ended_at")]
        public DateTime? EndedAt { get; set; }

        [Column("status")]
        public string? Status { get; set; }

        [Column("greeting_sent")]
        public bool GreetingSent { get; set; }

        [Column("total_messages")]
        public int TotalMessages { get; set; }

        [Column("ai_messages")]
        public int AiMessages { get; set; }

        [Column("first_response_time")]
        public int? FirstResponseTime { get; set; }

        [Column("rating")]
        public int? Rating { get; set; }

        [Column("closed_by_user_id")]
        public int? ClosedByUserId { get; set; }

        [ForeignKey(nameof(ClosedByUserId))]
        public User? ClosedByUser { get; set; }

        [Column("company_id")]
        public int CompanyId { get; set; }

        // se marca cuando el usuario elige "Hablar con un agente"
        [Column("agent_requested_at")]
        public DateTime? AgentRequestedAt { get; set; }

        // NUEVO: asignación de conversación a un agente (para take/transfer/release)
        [Column("assigned_user_id")]
        public int? AssignedUserId { get; set; }

        [ForeignKey(nameof(AssignedUserId))]
        public User? AssignedUser { get; set; }

        [Column("assigned_at")]
        public DateTime? AssignedAt { get; set; }

        [Column("assigned_by_user_id")]
        public int? AssignedByUserId { get; set; }

        [ForeignKey(nameof(AssignedByUserId))]
        public User? AssignedByUser { get; set; }

        public Contact Contact { get; set; } = null!;
    }
}
