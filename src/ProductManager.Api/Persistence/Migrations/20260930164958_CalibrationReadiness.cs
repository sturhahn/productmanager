using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductManager.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CalibrationReadiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PriorEvidenceBasis",
                table: "Parameters",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PriorEvidenceObservedFrom",
                table: "Parameters",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PriorEvidenceObservedThrough",
                table: "Parameters",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PriorEvidencePopulation",
                table: "Parameters",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PriorEvidenceRecordedThrough",
                table: "Parameters",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PriorEvidenceSourceCalibrationRunId",
                table: "Parameters",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ObservationsRecordedAfter",
                table: "CalibrationRuns",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PriorParametersJson",
                table: "CalibrationRuns",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriorEvidenceBasis",
                table: "Parameters");

            migrationBuilder.DropColumn(
                name: "PriorEvidenceObservedFrom",
                table: "Parameters");

            migrationBuilder.DropColumn(
                name: "PriorEvidenceObservedThrough",
                table: "Parameters");

            migrationBuilder.DropColumn(
                name: "PriorEvidencePopulation",
                table: "Parameters");

            migrationBuilder.DropColumn(
                name: "PriorEvidenceRecordedThrough",
                table: "Parameters");

            migrationBuilder.DropColumn(
                name: "PriorEvidenceSourceCalibrationRunId",
                table: "Parameters");

            migrationBuilder.DropColumn(
                name: "ObservationsRecordedAfter",
                table: "CalibrationRuns");

            migrationBuilder.DropColumn(
                name: "PriorParametersJson",
                table: "CalibrationRuns");
        }
    }
}
