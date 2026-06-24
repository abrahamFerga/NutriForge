using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriForge.Infrastructure.Persistence.Migrations.App
{
    /// <inheritdoc />
    public partial class AddChannelSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_link_tokens",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_link_tokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "channel_subscriptions",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Address = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    SendHourUtc = table.Column<int>(type: "integer", nullable: false),
                    LastSentOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_subscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_link_tokens_TokenHash",
                schema: "app",
                table: "account_link_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_channel_subscriptions_UserId_Channel",
                schema: "app",
                table: "channel_subscriptions",
                columns: new[] { "UserId", "Channel" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_link_tokens",
                schema: "app");

            migrationBuilder.DropTable(
                name: "channel_subscriptions",
                schema: "app");
        }
    }
}
