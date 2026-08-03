using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoveeController.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompositeShortcuts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShortcutReference",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShortcutId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReferencedShortcutId = table.Column<int>(type: "INTEGER", nullable: true),
                    DelaySeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    Order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShortcutReference", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShortcutReference_Shortcuts_ReferencedShortcutId",
                        column: x => x.ReferencedShortcutId,
                        principalTable: "Shortcuts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ShortcutReference_Shortcuts_ShortcutId",
                        column: x => x.ShortcutId,
                        principalTable: "Shortcuts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShortcutReference_ReferencedShortcutId",
                table: "ShortcutReference",
                column: "ReferencedShortcutId");

            migrationBuilder.CreateIndex(
                name: "IX_ShortcutReference_ShortcutId",
                table: "ShortcutReference",
                column: "ShortcutId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShortcutReference");
        }
    }
}
