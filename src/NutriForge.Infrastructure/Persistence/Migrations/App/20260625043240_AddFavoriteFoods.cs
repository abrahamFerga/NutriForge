using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriForge.Infrastructure.Persistence.Migrations.App
{
    /// <inheritdoc />
    public partial class AddFavoriteFoods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "favorite_foods",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FoodId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_favorite_foods", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_favorite_foods_UserId_FoodId",
                schema: "app",
                table: "favorite_foods",
                columns: new[] { "UserId", "FoodId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "favorite_foods",
                schema: "app");
        }
    }
}
