using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriForge.Infrastructure.Persistence.Migrations.App
{
    /// <inheritdoc />
    public partial class AddRecipeOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_recipes_SourceVideoId",
                schema: "catalog",
                table: "recipes");

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                schema: "catalog",
                table: "recipes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_recipes_OwnerUserId",
                schema: "catalog",
                table: "recipes",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_recipes_OwnerUserId_SourceVideoId",
                schema: "catalog",
                table: "recipes",
                columns: new[] { "OwnerUserId", "SourceVideoId" },
                unique: true,
                filter: "\"SourceVideoId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_recipes_OwnerUserId",
                schema: "catalog",
                table: "recipes");

            migrationBuilder.DropIndex(
                name: "IX_recipes_OwnerUserId_SourceVideoId",
                schema: "catalog",
                table: "recipes");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                schema: "catalog",
                table: "recipes");

            migrationBuilder.CreateIndex(
                name: "IX_recipes_SourceVideoId",
                schema: "catalog",
                table: "recipes",
                column: "SourceVideoId",
                unique: true,
                filter: "\"SourceVideoId\" IS NOT NULL");
        }
    }
}
