using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductManager.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OneDecisionFactPerOpportunity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_FactEvents_OneDecisionPerOpportunity",
                table: "FactEvents",
                column: "SubjectId",
                unique: true,
                filter: "\"Kind\" = 'OpportunityDecided'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FactEvents_OneDecisionPerOpportunity",
                table: "FactEvents");
        }
    }
}
