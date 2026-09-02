using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductManager.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInitiativeStrategicIntent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StrategicIntent",
                table: "Initiatives",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StrategicIntent",
                table: "Initiatives");
        }
    }
}
