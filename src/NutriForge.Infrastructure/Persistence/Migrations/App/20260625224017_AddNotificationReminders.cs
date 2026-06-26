using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriForge.Infrastructure.Persistence.Migrations.App
{
    /// <inheritdoc />
    public partial class AddNotificationReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReminderHourUtc",
                schema: "app",
                table: "channel_subscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReminderLastSentOn",
                schema: "app",
                table: "channel_subscriptions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "WeeklyLastSentOn",
                schema: "app",
                table: "channel_subscriptions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WeeklySummaryEnabled",
                schema: "app",
                table: "channel_subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReminderHourUtc",
                schema: "app",
                table: "channel_subscriptions");

            migrationBuilder.DropColumn(
                name: "ReminderLastSentOn",
                schema: "app",
                table: "channel_subscriptions");

            migrationBuilder.DropColumn(
                name: "WeeklyLastSentOn",
                schema: "app",
                table: "channel_subscriptions");

            migrationBuilder.DropColumn(
                name: "WeeklySummaryEnabled",
                schema: "app",
                table: "channel_subscriptions");
        }
    }
}
