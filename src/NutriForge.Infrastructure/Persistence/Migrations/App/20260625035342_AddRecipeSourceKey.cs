using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriForge.Infrastructure.Persistence.Migrations.App
{
    /// <inheritdoc />
    public partial class AddRecipeSourceKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceKey",
                schema: "catalog",
                table: "recipes",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_recipes_OwnerUserId_SourceKey",
                schema: "catalog",
                table: "recipes",
                columns: new[] { "OwnerUserId", "SourceKey" },
                unique: true,
                filter: "\"SourceKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_recipes_OwnerUserId_SourceKey",
                schema: "catalog",
                table: "recipes");

            migrationBuilder.DropColumn(
                name: "SourceKey",
                schema: "catalog",
                table: "recipes");
        }
    }
}
