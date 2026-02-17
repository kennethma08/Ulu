using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Whatsapp_API.Models.Entities.Security;
using Whatsapp_API.Models.Entities.System; 


namespace Whatsapp_API.Models.Entities.Messaging
{
    [Table("conversations")]
    [Index(nameof(CompanyId), nameof(IsOnHold), Name = "IX_conversations_company_id_is_on_hold")]
    public class Conversation
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("contact_id")]
        public int ContactId { get; set; }
        public Contact Contact { get; set; } = null!;

        [Column("started_at")]
        public DateTime StartedAt { get; set; }

        [Column("last_activity_at")]
        public DateTime? LastActivityAt { get; set; }

        [Column("ended_at")]
        public DateTime? EndedAt { get; set; }

        [Column("status")]
        [MaxLength(20)]
        public string? Status { get; set; } = "open";

        [Column("greeting_sent")]
        public bool GreetingSent { get; set; } = false;

        [Column("total_messages")]
        public int TotalMessages { get; set; } = 0;

        [Column("ai_messages")]
        public int AiMessages { get; set; } = 0;

        [Column("first_response_time")]
        public int? FirstResponseTime { get; set; }

        [Column("rating")]
        public int? Rating { get; set; }

        [Column("closed_by_user_id")]
        public int? ClosedByUserId { get; set; }

        public User? ClosedByUser { get; set; }

        [Column("company_id")]
        public int CompanyId { get; set; }

        // ====== HOLD ======
        [Column("is_on_hold")]
        public bool IsOnHold { get; set; } = false;

        [Column("on_hold_reason")]
        [MaxLength(500)]
        public string? OnHoldReason { get; set; }

        [Column("on_hold_at")]
        public DateTime? OnHoldAt { get; set; }

        [Column("on_hold_by_user_id")]
        public int? OnHoldByUserId { get; set; }

        // ====== AGENT REQUEST / ASSIGNMENT ======
        [Column("agent_requested_at")]
        public DateTime? AgentRequestedAt { get; set; }

        [Column("assigned_at")]
        public DateTime? AssignedAt { get; set; }

        [Column("assigned_by_user_id")]
        public int? AssignedByUserId { get; set; }

        [Column("assigned_user_id")]
        public int? AssignedUserId { get; set; }
    }
}
