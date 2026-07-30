using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoveeController.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Schedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShortcutId = table.Column<int>(type: "INTEGER", nullable: false),
                    DaysOfWeekMask = table.Column<int>(type: "INTEGER", nullable: false),
                    OneTimeDateLocal = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    TimeOfDayLocal = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    NextRunAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Schedules_Shortcuts_ShortcutId",
                        column: x => x.ShortcutId,
                        principalTable: "Shortcuts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Schedules_NextRunAtUtc",
                table: "Schedules",
                column: "NextRunAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Schedules_ShortcutId",
                table: "Schedules",
                column: "ShortcutId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Schedules");
        }
    }
}
