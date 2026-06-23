using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriForge.Infrastructure.Persistence.Migrations.App
{
    /// <inheritdoc />
    public partial class AddRecipesAndPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "diet_types",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequiredTags = table.Column<List<string>>(type: "text[]", nullable: false),
                    ExcludedKeywords = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_diet_types", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ingredients",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AisleCategory = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Aliases = table.Column<List<string>>(type: "text[]", nullable: false),
                    DensityGPerMl = table.Column<double>(type: "double precision", nullable: true),
                    DefaultFoodId = table.Column<Guid>(type: "uuid", nullable: true),
                    GramsPerCount = table.Column<double>(type: "double precision", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingredients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "meal_plans",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Desire = table.Column<string>(type: "text", nullable: true),
                    IntentJson = table.Column<string>(type: "jsonb", nullable: false),
                    HorizonDays = table.Column<int>(type: "integer", nullable: false),
                    TargetKcal = table.Column<double>(type: "double precision", nullable: false),
                    AchievedKcal = table.Column<double>(type: "double precision", nullable: false),
                    AchievedProteinG = table.Column<double>(type: "double precision", nullable: false),
                    AchievedFatG = table.Column<double>(type: "double precision", nullable: false),
                    AchievedCarbG = table.Column<double>(type: "double precision", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meal_plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pantry_items",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IngredientId = table.Column<Guid>(type: "uuid", nullable: false),
                    IngredientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Grams = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pantry_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "recipes",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Servings = table.Column<int>(type: "integer", nullable: false),
                    TotalMinutes = table.Column<int>(type: "integer", nullable: false),
                    Instructions = table.Column<string>(type: "text", nullable: true),
                    Tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    KcalPerServing = table.Column<double>(type: "double precision", nullable: false),
                    ProteinPerServing = table.Column<double>(type: "double precision", nullable: false),
                    FatPerServing = table.Column<double>(type: "double precision", nullable: false),
                    CarbPerServing = table.Column<double>(type: "double precision", nullable: false),
                    IsNutritionComputed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recipes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "shopping_lists",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MealPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shopping_lists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "plan_slots",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MealPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<int>(type: "integer", nullable: false),
                    MealSlot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RecipeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipeName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Servings = table.Column<double>(type: "double precision", nullable: false),
                    Kcal = table.Column<double>(type: "double precision", nullable: false),
                    ProteinG = table.Column<double>(type: "double precision", nullable: false),
                    FatG = table.Column<double>(type: "double precision", nullable: false),
                    CarbG = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_slots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_plan_slots_meal_plans_MealPlanId",
                        column: x => x.MealPlanId,
                        principalSchema: "app",
                        principalTable: "meal_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recipe_ingredients",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawText = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<double>(type: "double precision", nullable: false),
                    Unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    IngredientId = table.Column<Guid>(type: "uuid", nullable: true),
                    IngredientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Grams = table.Column<double>(type: "double precision", nullable: false),
                    Resolved = table.Column<bool>(type: "boolean", nullable: false),
                    Kcal = table.Column<double>(type: "double precision", nullable: false),
                    ProteinG = table.Column<double>(type: "double precision", nullable: false),
                    FatG = table.Column<double>(type: "double precision", nullable: false),
                    CarbG = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recipe_ingredients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_recipe_ingredients_recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalSchema: "catalog",
                        principalTable: "recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shopping_items",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShoppingListId = table.Column<Guid>(type: "uuid", nullable: false),
                    IngredientId = table.Column<Guid>(type: "uuid", nullable: true),
                    IngredientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AisleCategory = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Grams = table.Column<double>(type: "double precision", nullable: false),
                    PantryCovered = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shopping_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shopping_items_shopping_lists_ShoppingListId",
                        column: x => x.ShoppingListId,
                        principalSchema: "app",
                        principalTable: "shopping_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_diet_types_Slug",
                schema: "catalog",
                table: "diet_types",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ingredients_CanonicalName",
                schema: "catalog",
                table: "ingredients",
                column: "CanonicalName");

            migrationBuilder.CreateIndex(
                name: "IX_meal_plans_UserId_Status",
                schema: "app",
                table: "meal_plans",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_pantry_items_UserId",
                schema: "app",
                table: "pantry_items",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_plan_slots_MealPlanId",
                schema: "app",
                table: "plan_slots",
                column: "MealPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_recipe_ingredients_RecipeId",
                schema: "catalog",
                table: "recipe_ingredients",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_recipes_IsNutritionComputed",
                schema: "catalog",
                table: "recipes",
                column: "IsNutritionComputed");

            migrationBuilder.CreateIndex(
                name: "IX_shopping_items_ShoppingListId",
                schema: "app",
                table: "shopping_items",
                column: "ShoppingListId");

            migrationBuilder.CreateIndex(
                name: "IX_shopping_lists_UserId",
                schema: "app",
                table: "shopping_lists",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "diet_types",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "ingredients",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "pantry_items",
                schema: "app");

            migrationBuilder.DropTable(
                name: "plan_slots",
                schema: "app");

            migrationBuilder.DropTable(
                name: "recipe_ingredients",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "shopping_items",
                schema: "app");

            migrationBuilder.DropTable(
                name: "meal_plans",
                schema: "app");

            migrationBuilder.DropTable(
                name: "recipes",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "shopping_lists",
                schema: "app");
        }
    }
}
