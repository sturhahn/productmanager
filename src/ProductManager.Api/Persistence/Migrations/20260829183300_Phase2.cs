using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProductManager.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DiscountRateAnnual",
                table: "SimulationRuns",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<long>(
                name: "ElapsedMs",
                table: "SimulationRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HorizonMonths",
                table: "SimulationRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "SimulationRuns",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Paths",
                table: "SimulationRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "SimulationRuns",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<double>(
                name: "TerminalGrowthAnnual",
                table: "SimulationRuns",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientSegmentId",
                table: "Parameters",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CovariateBeta",
                table: "Parameters",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CovariateId",
                table: "Parameters",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FlowKind",
                table: "Parameters",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModuleProductId",
                table: "Parameters",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GbmDrift",
                table: "Covariates",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GbmStart",
                table: "Covariates",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GbmVol",
                table: "Covariates",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ArpuPerMonth",
                table: "ClientSegments",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "ImplementationFee",
                table: "ClientSegments",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "InfraCostPerClient",
                table: "ClientSegments",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "InfraFixedCostPerMonth",
                table: "ClientSegments",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "InitialClientCount",
                table: "ClientSegments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "InitialProspectPool",
                table: "ClientSegments",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "RdDragPerMonth",
                table: "ClientSegments",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "ServicingCostPerClient",
                table: "ClientSegments",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.CreateTable(
                name: "ClientSegmentModulePenetrations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ModuleProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Penetration = table.Column<double>(type: "double precision", nullable: false),
                    ArpuPerMonth = table.Column<double>(type: "double precision", nullable: false),
                    ClientSegmentId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientSegmentModulePenetrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientSegmentModulePenetrations_ClientSegments_ClientSegmen~",
                        column: x => x.ClientSegmentId,
                        principalTable: "ClientSegments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MarketEventRateMultipliers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClientSegmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    FlowKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Multiplier = table.Column<double>(type: "double precision", nullable: false),
                    MarketEventId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketEventRateMultipliers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketEventRateMultipliers_MarketEvents_MarketEventId",
                        column: x => x.MarketEventId,
                        principalTable: "MarketEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Parameters_ClientSegmentId_FlowKind",
                table: "Parameters",
                columns: new[] { "ClientSegmentId", "FlowKind" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketEvents_IsHypothetical",
                table: "MarketEvents",
                column: "IsHypothetical");

            migrationBuilder.CreateIndex(
                name: "IX_ClientSegmentModulePenetrations_ClientSegmentId",
                table: "ClientSegmentModulePenetrations",
                column: "ClientSegmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientSegmentModulePenetrations_ModuleProductId",
                table: "ClientSegmentModulePenetrations",
                column: "ModuleProductId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketEventRateMultipliers_ClientSegmentId",
                table: "MarketEventRateMultipliers",
                column: "ClientSegmentId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketEventRateMultipliers_MarketEventId",
                table: "MarketEventRateMultipliers",
                column: "MarketEventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientSegmentModulePenetrations");

            migrationBuilder.DropTable(
                name: "MarketEventRateMultipliers");

            migrationBuilder.DropIndex(
                name: "IX_Parameters_ClientSegmentId_FlowKind",
                table: "Parameters");

            migrationBuilder.DropIndex(
                name: "IX_MarketEvents_IsHypothetical",
                table: "MarketEvents");

            migrationBuilder.DropColumn(
                name: "DiscountRateAnnual",
                table: "SimulationRuns");

            migrationBuilder.DropColumn(
                name: "ElapsedMs",
                table: "SimulationRuns");

            migrationBuilder.DropColumn(
                name: "HorizonMonths",
                table: "SimulationRuns");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "SimulationRuns");

            migrationBuilder.DropColumn(
                name: "Paths",
                table: "SimulationRuns");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "SimulationRuns");

            migrationBuilder.DropColumn(
                name: "TerminalGrowthAnnual",
                table: "SimulationRuns");

            migrationBuilder.DropColumn(
                name: "ClientSegmentId",
                table: "Parameters");

            migrationBuilder.DropColumn(
                name: "CovariateBeta",
                table: "Parameters");

            migrationBuilder.DropColumn(
                name: "CovariateId",
                table: "Parameters");

            migrationBuilder.DropColumn(
                name: "FlowKind",
                table: "Parameters");

            migrationBuilder.DropColumn(
                name: "ModuleProductId",
                table: "Parameters");

            migrationBuilder.DropColumn(
                name: "GbmDrift",
                table: "Covariates");

            migrationBuilder.DropColumn(
                name: "GbmStart",
                table: "Covariates");

            migrationBuilder.DropColumn(
                name: "GbmVol",
                table: "Covariates");

            migrationBuilder.DropColumn(
                name: "ArpuPerMonth",
                table: "ClientSegments");

            migrationBuilder.DropColumn(
                name: "ImplementationFee",
                table: "ClientSegments");

            migrationBuilder.DropColumn(
                name: "InfraCostPerClient",
                table: "ClientSegments");

            migrationBuilder.DropColumn(
                name: "InfraFixedCostPerMonth",
                table: "ClientSegments");

            migrationBuilder.DropColumn(
                name: "InitialClientCount",
                table: "ClientSegments");

            migrationBuilder.DropColumn(
                name: "InitialProspectPool",
                table: "ClientSegments");

            migrationBuilder.DropColumn(
                name: "RdDragPerMonth",
                table: "ClientSegments");

            migrationBuilder.DropColumn(
                name: "ServicingCostPerClient",
                table: "ClientSegments");
        }
    }
}
