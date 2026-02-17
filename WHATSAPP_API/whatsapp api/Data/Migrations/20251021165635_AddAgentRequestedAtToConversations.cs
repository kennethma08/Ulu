using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Whatsapp_API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentRequestedAtToConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "agent_requested_at",
                table: "conversations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_conversations_empresa_id_agent_requested_at",
                table: "conversations",
                columns: new[] { "empresa_id", "agent_requested_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_conversations_empresa_id_agent_requested_at",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "agent_requested_at",
                table: "conversations");
        }
    }
}
