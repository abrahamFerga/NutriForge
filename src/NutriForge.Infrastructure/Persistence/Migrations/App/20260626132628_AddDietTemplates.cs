using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriForge.Infrastructure.Persistence.Migrations.App
{
    /// <inheritdoc />
    public partial class AddDietTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "diet_templates",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DietSlug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    KcalTarget = table.Column<double>(type: "double precision", nullable: true),
                    MaxPrepMinutes = table.Column<int>(type: "integer", nullable: true),
                    MealsPerDay = table.Column<int>(type: "integer", nullable: true),
                    HorizonDays = table.Column<int>(type: "integer", nullable: false),
                    BlockSize = table.Column<int>(type: "integer", nullable: false),
                    Desire = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_diet_templates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_diet_templates_UserId",
                schema: "app",
                table: "diet_templates",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "diet_templates",
                schema: "app");
        }
    }
}
