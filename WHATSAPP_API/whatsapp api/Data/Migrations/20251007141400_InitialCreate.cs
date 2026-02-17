using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Whatsapp_API.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contacts",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    phone_number = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    country = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ip_address = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_message_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    profile_pic = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    welcome_sent = table.Column<bool>(type: "bit", nullable: false),
                    empresa_id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "empresas",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    nombre = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FlowKey = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_empresas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "integrations",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    provider = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    phone_number_id = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    waba_id = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    access_token_enc = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    verify_token_hash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    api_base_url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    api_version = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    empresa_id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integrations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "profiles",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    nombre = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    empresa_id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    nombre = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    correo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    cedula = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    pass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    telefono = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    estado = table.Column<bool>(type: "bit", nullable: true),
                    idPerfil = table.Column<int>(type: "int", nullable: true),
                    empresa = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    agent_id = table.Column<int>(type: "int", nullable: true),
                    empresa_id = table.Column<int>(type: "int", nullable: true),
                    contact_id = table.Column<int>(type: "int", nullable: true),
                    last_login = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_activity = table.Column<DateTime>(type: "datetime2", nullable: true),
                    is_online = table.Column<bool>(type: "bit", nullable: false),
                    conversation_count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usuarios", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "WhatsappTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmpresaId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BodyParamCount = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsappTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    contact_id = table.Column<int>(type: "int", nullable: false),
                    agent_id = table.Column<int>(type: "int", nullable: true),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    last_activity_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ended_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    status = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    greeting_sent = table.Column<bool>(type: "bit", nullable: false),
                    total_messages = table.Column<int>(type: "int", nullable: false),
                    ai_messages = table.Column<int>(type: "int", nullable: false),
                    first_response_time = table.Column<int>(type: "int", nullable: true),
                    rating = table.Column<int>(type: "int", nullable: true),
                    closed_by_user_id = table.Column<int>(type: "int", nullable: true),
                    empresa_id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversations", x => x.id);
                    table.ForeignKey(
                        name: "FK_conversations_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_conversations_usuarios_closed_by_user_id",
                        column: x => x.closed_by_user_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    conversation_id = table.Column<int>(type: "int", nullable: false),
                    contact_id = table.Column<int>(type: "int", nullable: false),
                    agent_id = table.Column<int>(type: "int", nullable: true),
                    sender = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    sent_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    latitude = table.Column<decimal>(type: "decimal(10,6)", nullable: true),
                    longitude = table.Column<decimal>(type: "decimal(10,6)", nullable: true),
                    location_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    empresa_id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_messages_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attachments",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    message_id = table.Column<int>(type: "int", nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    mime_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    data = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    uploaded_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    empresa_id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attachments", x => x.id);
                    table.ForeignKey(
                        name: "FK_attachments_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attachments_message_id",
                table: "attachments",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_closed_by_user_id_ended_at",
                table: "conversations",
                columns: new[] { "closed_by_user_id", "ended_at" });

            migrationBuilder.CreateIndex(
                name: "IX_conversations_contact_id_started_at",
                table: "conversations",
                columns: new[] { "contact_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_messages_contact_id",
                table: "messages",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_conversation_id_sent_at",
                table: "messages",
                columns: new[] { "conversation_id", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsappTemplates_EmpresaId_Name_Language",
                table: "WhatsappTemplates",
                columns: new[] { "EmpresaId", "Name", "Language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attachments");

            migrationBuilder.DropTable(
                name: "empresas");

            migrationBuilder.DropTable(
                name: "integrations");

            migrationBuilder.DropTable(
                name: "profiles");

            migrationBuilder.DropTable(
                name: "WhatsappTemplates");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "conversations");

            migrationBuilder.DropTable(
                name: "contacts");

            migrationBuilder.DropTable(
                name: "usuarios");
        }
    }
}
