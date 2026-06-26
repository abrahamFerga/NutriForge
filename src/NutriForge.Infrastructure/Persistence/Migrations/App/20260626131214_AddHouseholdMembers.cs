using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriForge.Infrastructure.Persistence.Migrations.App
{
    /// <inheritdoc />
    public partial class AddHouseholdMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "household_members",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Relationship = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    TargetKcal = table.Column<double>(type: "double precision", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_household_members", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_household_members_UserId",
                schema: "app",
                table: "household_members",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "household_members",
                schema: "app");
        }
    }
}
