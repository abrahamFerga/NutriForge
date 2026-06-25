using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriForge.Infrastructure.Persistence.Migrations.App
{
    /// <inheritdoc />
    public partial class AddRecipeSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                schema: "catalog",
                table: "recipes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                schema: "catalog",
                table: "recipes",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceVideoId",
                schema: "catalog",
                table: "recipes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailUrl",
                schema: "catalog",
                table: "recipes",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_recipes_SourceVideoId",
                schema: "catalog",
                table: "recipes",
                column: "SourceVideoId",
                unique: true,
                filter: "\"SourceVideoId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_recipes_SourceVideoId",
                schema: "catalog",
                table: "recipes");

            migrationBuilder.DropColumn(
                name: "SourceType",
                schema: "catalog",
                table: "recipes");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                schema: "catalog",
                table: "recipes");

            migrationBuilder.DropColumn(
                name: "SourceVideoId",
                schema: "catalog",
                table: "recipes");

            migrationBuilder.DropColumn(
                name: "ThumbnailUrl",
                schema: "catalog",
                table: "recipes");
        }
    }
}
