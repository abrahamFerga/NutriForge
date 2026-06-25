using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriForge.Infrastructure.Persistence.Migrations.App
{
    /// <inheritdoc />
    public partial class AddPlanBlockSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BlockSize",
                schema: "app",
                table: "meal_plans",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlockSize",
                schema: "app",
                table: "meal_plans");
        }
    }
}
