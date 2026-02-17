using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Whatsapp_API.Data.Migrations
{
    /// <inheritdoc />
    public partial class CodeLanguageChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_conversations_usuarios_closed_by_user_id",
                table: "conversations");

            migrationBuilder.DropTable(
                name: "empresas");

            migrationBuilder.DropTable(
                name: "usuarios");

            migrationBuilder.RenameColumn(
                name: "EmpresaId",
                table: "WhatsappTemplates",
                newName: "CompanyId");

            migrationBuilder.RenameIndex(
                name: "IX_WhatsappTemplates_EmpresaId_Name_Language",
                table: "WhatsappTemplates",
                newName: "IX_WhatsappTemplates_CompanyId_Name_Language");

            migrationBuilder.RenameColumn(
                name: "nombre",
                table: "profiles",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "empresa_id",
                table: "profiles",
                newName: "company_id");

            migrationBuilder.RenameColumn(
                name: "empresa_id",
                table: "messages",
                newName: "company_id");

            migrationBuilder.RenameColumn(
                name: "empresa_id",
                table: "integrations",
                newName: "company_id");

            migrationBuilder.RenameColumn(
                name: "empresa_id",
                table: "conversations",
                newName: "company_id");

            migrationBuilder.RenameIndex(
                name: "IX_conversations_empresa_id_agent_requested_at",
                table: "conversations",
                newName: "IX_conversations_company_id_agent_requested_at");

            migrationBuilder.RenameColumn(
                name: "empresa_id",
                table: "contacts",
                newName: "company_id");

            migrationBuilder.RenameColumn(
                name: "empresa_id",
                table: "attachments",
                newName: "company_id");

            migrationBuilder.CreateTable(
                name: "companies",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FlowKey = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    pass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    phone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<bool>(type: "bit", nullable: true),
                    idProfile = table.Column<int>(type: "int", nullable: true),
                    company = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    company_id = table.Column<int>(type: "int", nullable: true),
                    contact_id = table.Column<int>(type: "int", nullable: true),
                    last_login = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_activity = table.Column<DateTime>(type: "datetime2", nullable: true),
                    is_online = table.Column<bool>(type: "bit", nullable: false),
                    avatar_mime_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    avatar_file_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    avatar_updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    conversation_count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.AddForeignKey(
                name: "FK_conversations_users_closed_by_user_id",
                table: "conversations",
                column: "closed_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_conversations_users_closed_by_user_id",
                table: "conversations");

            migrationBuilder.DropTable(
                name: "companies");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                table: "WhatsappTemplates",
                newName: "EmpresaId");

            migrationBuilder.RenameIndex(
                name: "IX_WhatsappTemplates_CompanyId_Name_Language",
                table: "WhatsappTemplates",
                newName: "IX_WhatsappTemplates_EmpresaId_Name_Language");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "profiles",
                newName: "nombre");

            migrationBuilder.RenameColumn(
                name: "company_id",
                table: "profiles",
                newName: "empresa_id");

            migrationBuilder.RenameColumn(
                name: "company_id",
                table: "messages",
                newName: "empresa_id");

            migrationBuilder.RenameColumn(
                name: "company_id",
                table: "integrations",
                newName: "empresa_id");

            migrationBuilder.RenameColumn(
                name: "company_id",
                table: "conversations",
                newName: "empresa_id");

            migrationBuilder.RenameIndex(
                name: "IX_conversations_company_id_agent_requested_at",
                table: "conversations",
                newName: "IX_conversations_empresa_id_agent_requested_at");

            migrationBuilder.RenameColumn(
                name: "company_id",
                table: "contacts",
                newName: "empresa_id");

            migrationBuilder.RenameColumn(
                name: "company_id",
                table: "attachments",
                newName: "empresa_id");

            migrationBuilder.CreateTable(
                name: "empresas",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FlowKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    nombre = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_empresas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    avatar_file_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    avatar_mime_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    avatar_updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    contact_id = table.Column<int>(type: "int", nullable: true),
                    conversation_count = table.Column<int>(type: "int", nullable: false),
                    correo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    empresa = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    empresa_id = table.Column<int>(type: "int", nullable: true),
                    estado = table.Column<bool>(type: "bit", nullable: true),
                    idPerfil = table.Column<int>(type: "int", nullable: true),
                    is_online = table.Column<bool>(type: "bit", nullable: false),
                    last_activity = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_login = table.Column<DateTime>(type: "datetime2", nullable: true),
                    nombre = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    pass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    telefono = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usuarios", x => x.id);
                });

            migrationBuilder.AddForeignKey(
                name: "FK_conversations_usuarios_closed_by_user_id",
                table: "conversations",
                column: "closed_by_user_id",
                principalTable: "usuarios",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
