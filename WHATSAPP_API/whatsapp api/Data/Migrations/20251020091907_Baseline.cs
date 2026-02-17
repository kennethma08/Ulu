using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Whatsapp_API.Data.Migrations
{
    /// <inheritdoc />
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "agent_id",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "agent_id",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "agent_id",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "ip_address",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "profile_pic",
                table: "contacts");

            migrationBuilder.RenameColumn(
                name: "Category",
                table: "WhatsappTemplates",
                newName: "TemplateIdentifier");

            migrationBuilder.RenameColumn(
                name: "cedula",
                table: "usuarios",
                newName: "avatar_mime_type");

            migrationBuilder.AddColumn<string>(
                name: "avatar_file_name",
                table: "usuarios",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "avatar_updated_at",
                table: "usuarios",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "last_location_latitude",
                table: "contacts",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "last_location_longitude",
                table: "contacts",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "avatar_file_name",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "avatar_updated_at",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "last_location_latitude",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "last_location_longitude",
                table: "contacts");

            migrationBuilder.RenameColumn(
                name: "TemplateIdentifier",
                table: "WhatsappTemplates",
                newName: "Category");

            migrationBuilder.RenameColumn(
                name: "avatar_mime_type",
                table: "usuarios",
                newName: "cedula");

            migrationBuilder.AddColumn<int>(
                name: "agent_id",
                table: "usuarios",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "agent_id",
                table: "messages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "agent_id",
                table: "conversations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ip_address",
                table: "contacts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "profile_pic",
                table: "contacts",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
