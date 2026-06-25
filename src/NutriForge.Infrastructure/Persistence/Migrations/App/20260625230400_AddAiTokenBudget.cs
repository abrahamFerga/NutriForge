using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriForge.Infrastructure.Persistence.Migrations.App
{
    /// <inheritdoc />
    public partial class AddAiTokenBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "BudgetResetMonth",
                schema: "app",
                table: "assistant_sessions",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<long>(
                name: "TokensUsedThisMonth",
                schema: "app",
                table: "assistant_sessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BudgetResetMonth",
                schema: "app",
                table: "assistant_sessions");

            migrationBuilder.DropColumn(
                name: "TokensUsedThisMonth",
                schema: "app",
                table: "assistant_sessions");
        }
    }
}
