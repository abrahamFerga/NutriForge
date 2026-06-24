using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriForge.Infrastructure.Persistence.Migrations.App
{
    /// <inheritdoc />
    public partial class AddIngredientYield : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RecipeGramsAreRaw",
                schema: "catalog",
                table: "ingredients",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<double>(
                name: "YieldFactor",
                schema: "catalog",
                table: "ingredients",
                type: "double precision",
                nullable: false,
                defaultValue: 1.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecipeGramsAreRaw",
                schema: "catalog",
                table: "ingredients");

            migrationBuilder.DropColumn(
                name: "YieldFactor",
                schema: "catalog",
                table: "ingredients");
        }
    }
}
