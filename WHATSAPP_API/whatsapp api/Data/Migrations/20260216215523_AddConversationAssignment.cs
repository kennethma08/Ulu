using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Whatsapp_API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "assigned_at",
                table: "conversations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "assigned_by_user_id",
                table: "conversations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "assigned_user_id",
                table: "conversations",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_conversations_assigned_by_user_id",
                table: "conversations",
                column: "assigned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_assigned_user_id",
                table: "conversations",
                column: "assigned_user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_conversations_users_assigned_by_user_id",
                table: "conversations",
                column: "assigned_by_user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_conversations_users_assigned_user_id",
                table: "conversations",
                column: "assigned_user_id",
                principalTable: "users",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_conversations_users_assigned_by_user_id",
                table: "conversations");

            migrationBuilder.DropForeignKey(
                name: "FK_conversations_users_assigned_user_id",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "IX_conversations_assigned_by_user_id",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "IX_conversations_assigned_user_id",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "assigned_at",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "assigned_by_user_id",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "assigned_user_id",
                table: "conversations");
        }
    }
}
