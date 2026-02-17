using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Whatsapp_API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationHoldAndAssignFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "conversations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_on_hold",
                table: "conversations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "on_hold_at",
                table: "conversations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "on_hold_by_user_id",
                table: "conversations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "on_hold_reason",
                table: "conversations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_conversations_company_id_is_on_hold",
                table: "conversations",
                columns: new[] { "company_id", "is_on_hold" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_conversations_company_id_is_on_hold",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "is_on_hold",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "on_hold_at",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "on_hold_by_user_id",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "on_hold_reason",
                table: "conversations");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "conversations",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

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
    }
}
