using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Otools.Net.Sfe.Core.Data.AccessMigrations
{
    /// <inheritdoc />
    public partial class HomeFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HomeFolder",
                schema: "SFE",
                table: "Groups",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HomeFolders",
                schema: "SFE",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Path = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomeFolders", x => new { x.UserId, x.TenantId });
                    table.ForeignKey(
                        name: "FK_HomeFolders_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "SFE",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HomeFolders_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "SFE",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HomeFolders_TenantId",
                schema: "SFE",
                table: "HomeFolders",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HomeFolders",
                schema: "SFE");

            migrationBuilder.DropColumn(
                name: "HomeFolder",
                schema: "SFE",
                table: "Groups");
        }
    }
}
