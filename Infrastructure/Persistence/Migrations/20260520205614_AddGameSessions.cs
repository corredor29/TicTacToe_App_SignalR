using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGameSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "game_sessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoomName = table.Column<string>(type: "text", nullable: false),
                    PlayerX = table.Column<string>(type: "text", nullable: false),
                    PlayerO = table.Column<string>(type: "text", nullable: false),
                    CurrentTurnUser = table.Column<string>(type: "text", nullable: false),
                    CurrentTurnSymbol = table.Column<string>(type: "text", nullable: false),
                    BoardState = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Winner = table.Column<string>(type: "text", nullable: true),
                    WinningSymbol = table.Column<string>(type: "text", nullable: true),
                    WinningPositions = table.Column<string>(type: "text", nullable: false),
                    LastMoveBy = table.Column<string>(type: "text", nullable: true),
                    LastMovePosition = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_sessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_game_sessions_RoomName",
                table: "game_sessions",
                column: "RoomName");

            migrationBuilder.CreateIndex(
                name: "IX_game_sessions_RoomName_IsActive",
                table: "game_sessions",
                columns: new[] { "RoomName", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_sessions");
        }
    }
}
