using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoveeController.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkedShortcuts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NextShortcutDelaySeconds",
                table: "Shortcuts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NextShortcutId",
                table: "Shortcuts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shortcuts_NextShortcutId",
                table: "Shortcuts",
                column: "NextShortcutId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Shortcuts_Shortcuts_NextShortcutId",
                table: "Shortcuts",
                column: "NextShortcutId",
                principalTable: "Shortcuts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Shortcuts_Shortcuts_NextShortcutId",
                table: "Shortcuts");

            migrationBuilder.DropIndex(
                name: "IX_Shortcuts_NextShortcutId",
                table: "Shortcuts");

            migrationBuilder.DropColumn(
                name: "NextShortcutDelaySeconds",
                table: "Shortcuts");

            migrationBuilder.DropColumn(
                name: "NextShortcutId",
                table: "Shortcuts");
        }
    }
}
