using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProductManager.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiagnosticsJson",
                table: "SimulationRuns",
                type: "jsonb",
                nullable: true);

            // Every run that already exists was executed by the aggregate engine — backfilling the
            // empty string would leave stored history claiming a mode that does not exist.
            migrationBuilder.AddColumn<string>(
                name: "EngineMode",
                table: "SimulationRuns",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Aggregate");

            migrationBuilder.AddColumn<Guid>(
                name: "CrossSellModuleProductId",
                table: "Opportunities",
                type: "uuid",
                nullable: true);

            // A relationship nobody has priced is at list price, not free: 1.0, never 0.
            migrationBuilder.AddColumn<double>(
                name: "PriceLevel",
                table: "FirmProductRelationships",
                type: "double precision",
                nullable: false,
                defaultValue: 1.0);

            // Existing relationships have no decomposition into modules yet: the empty array reads
            // as "whatever the product covers", which is what the builder does with it.
            migrationBuilder.AddColumn<List<Guid>>(
                name: "ServedProcessIds",
                table: "FirmProductRelationships",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<double>(
                name: "OriginationShare",
                table: "ChannelSegmentReaches",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<bool>(
                name: "CarriesOnlyListedProducts",
                table: "Channels",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "InputSnapshotJson",
                table: "CalibrationRuns",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "CalibrationRuns",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutputJson",
                table: "CalibrationRuns",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClientSegmentApproachAdoptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApproachId = table.Column<Guid>(type: "uuid", nullable: false),
                    Share = table.Column<double>(type: "double precision", nullable: false),
                    ClientSegmentId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientSegmentApproachAdoptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientSegmentApproachAdoptions_ClientSegments_ClientSegment~",
                        column: x => x.ClientSegmentId,
                        principalTable: "ClientSegments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Opportunities_CrossSellModuleProductId",
                table: "Opportunities",
                column: "CrossSellModuleProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CalibrationRuns_CreatedAt",
                table: "CalibrationRuns",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ClientSegmentApproachAdoptions_ApproachId",
                table: "ClientSegmentApproachAdoptions",
                column: "ApproachId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientSegmentApproachAdoptions_ClientSegmentId",
                table: "ClientSegmentApproachAdoptions",
                column: "ClientSegmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Opportunities_Products_CrossSellModuleProductId",
                table: "Opportunities",
                column: "CrossSellModuleProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Opportunities_Products_CrossSellModuleProductId",
                table: "Opportunities");

            migrationBuilder.DropTable(
                name: "ClientSegmentApproachAdoptions");

            migrationBuilder.DropIndex(
                name: "IX_Opportunities_CrossSellModuleProductId",
                table: "Opportunities");

            migrationBuilder.DropIndex(
                name: "IX_CalibrationRuns_CreatedAt",
                table: "CalibrationRuns");

            migrationBuilder.DropColumn(
                name: "DiagnosticsJson",
                table: "SimulationRuns");

            migrationBuilder.DropColumn(
                name: "EngineMode",
                table: "SimulationRuns");

            migrationBuilder.DropColumn(
                name: "CrossSellModuleProductId",
                table: "Opportunities");

            migrationBuilder.DropColumn(
                name: "PriceLevel",
                table: "FirmProductRelationships");

            migrationBuilder.DropColumn(
                name: "ServedProcessIds",
                table: "FirmProductRelationships");

            migrationBuilder.DropColumn(
                name: "OriginationShare",
                table: "ChannelSegmentReaches");

            migrationBuilder.DropColumn(
                name: "CarriesOnlyListedProducts",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "InputSnapshotJson",
                table: "CalibrationRuns");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "CalibrationRuns");

            migrationBuilder.DropColumn(
                name: "OutputJson",
                table: "CalibrationRuns");
        }
    }
}
