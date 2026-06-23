using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriForge.Infrastructure.Persistence.Migrations.App
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "diary_entries",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    MealSlot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    FoodId = table.Column<Guid>(type: "uuid", nullable: false),
                    FoodName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PortionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PortionName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Quantity = table.Column<double>(type: "double precision", nullable: false),
                    Grams = table.Column<double>(type: "double precision", nullable: false),
                    Kcal = table.Column<double>(type: "double precision", nullable: false),
                    ProteinG = table.Column<double>(type: "double precision", nullable: false),
                    FatG = table.Column<double>(type: "double precision", nullable: false),
                    CarbG = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_diary_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "foods",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Brand = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Gtin = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    VerificationStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_provider_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CanonicalIngredientId = table.Column<Guid>(type: "uuid", nullable: true),
                    kcal_per_100g = table.Column<double>(type: "double precision", nullable: false),
                    protein_per_100g = table.Column<double>(type: "double precision", nullable: false),
                    fat_per_100g = table.Column<double>(type: "double precision", nullable: false),
                    carb_per_100g = table.Column<double>(type: "double precision", nullable: false),
                    fiber_per_100g = table.Column<double>(type: "double precision", nullable: true),
                    sugar_per_100g = table.Column<double>(type: "double precision", nullable: true),
                    sodium_mg_per_100g = table.Column<double>(type: "double precision", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_foods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    ResponseBody = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "profiles",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sex = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    BirthDate = table.Column<DateOnly>(type: "date", nullable: false),
                    HeightCm = table.Column<double>(type: "double precision", nullable: false),
                    WeightKg = table.Column<double>(type: "double precision", nullable: false),
                    BodyFatPct = table.Column<double>(type: "double precision", nullable: true),
                    Activity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Goal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MacroStrategy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Allergens = table.Column<List<string>>(type: "text[]", nullable: false),
                    Dislikes = table.Column<List<string>>(type: "text[]", nullable: false),
                    PreferredDiets = table.Column<List<string>>(type: "text[]", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "targets",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kcal = table.Column<double>(type: "double precision", nullable: false),
                    ProteinG = table.Column<double>(type: "double precision", nullable: false),
                    FatG = table.Column<double>(type: "double precision", nullable: false),
                    CarbG = table.Column<double>(type: "double precision", nullable: false),
                    Formula = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ProfileVersion = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_targets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OidcSubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "portions",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FoodId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Grams = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_portions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_portions_foods_FoodId",
                        column: x => x.FoodId,
                        principalSchema: "catalog",
                        principalTable: "foods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_diary_entries_UserId_Date",
                schema: "app",
                table: "diary_entries",
                columns: new[] { "UserId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_foods_Gtin",
                schema: "catalog",
                table: "foods",
                column: "Gtin");

            migrationBuilder.CreateIndex(
                name: "IX_foods_source_provider_source_provider_id",
                schema: "catalog",
                table: "foods",
                columns: new[] { "source_provider", "source_provider_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_Key_UserId",
                schema: "app",
                table: "idempotency",
                columns: new[] { "Key", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_ProcessedAt",
                schema: "app",
                table: "outbox",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_portions_FoodId",
                schema: "catalog",
                table: "portions",
                column: "FoodId");

            migrationBuilder.CreateIndex(
                name: "IX_profiles_UserId",
                schema: "app",
                table: "profiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_targets_UserId",
                schema: "app",
                table: "targets",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_OidcSubject",
                schema: "app",
                table: "users",
                column: "OidcSubject",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "diary_entries",
                schema: "app");

            migrationBuilder.DropTable(
                name: "idempotency",
                schema: "app");

            migrationBuilder.DropTable(
                name: "outbox",
                schema: "app");

            migrationBuilder.DropTable(
                name: "portions",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "profiles",
                schema: "app");

            migrationBuilder.DropTable(
                name: "targets",
                schema: "app");

            migrationBuilder.DropTable(
                name: "users",
                schema: "app");

            migrationBuilder.DropTable(
                name: "foods",
                schema: "catalog");
        }
    }
}
