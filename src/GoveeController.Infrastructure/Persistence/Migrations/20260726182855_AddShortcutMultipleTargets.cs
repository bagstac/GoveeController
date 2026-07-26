using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoveeController.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShortcutMultipleTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "Shortcuts");

            migrationBuilder.DropColumn(
                name: "DeviceSku",
                table: "Shortcuts");

            migrationBuilder.CreateTable(
                name: "ShortcutTarget",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShortcutId = table.Column<int>(type: "INTEGER", nullable: false),
                    DeviceSku = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShortcutTarget", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShortcutTarget_Shortcuts_ShortcutId",
                        column: x => x.ShortcutId,
                        principalTable: "Shortcuts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShortcutTarget_ShortcutId",
                table: "ShortcutTarget",
                column: "ShortcutId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShortcutTarget");

            migrationBuilder.AddColumn<string>(
                name: "DeviceId",
                table: "Shortcuts",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DeviceSku",
                table: "Shortcuts",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");
        }
    }
}
