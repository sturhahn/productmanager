using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductManager.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReliableSourceImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SourceBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceInstance = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ObjectKind = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceRecordId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportedValuesJson = table.Column<string>(type: "jsonb", nullable: true),
                    BoundAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceBindings_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceBindings_TargetId",
                table: "SourceBindings",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceBindings_WorkspaceId_SourceSystem_SourceInstance_Obje~",
                table: "SourceBindings",
                columns: new[] { "WorkspaceId", "SourceSystem", "SourceInstance", "ObjectKind", "SourceRecordId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SourceBindings");
        }
    }
}
