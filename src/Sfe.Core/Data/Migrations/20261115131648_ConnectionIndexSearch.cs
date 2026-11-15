using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Otools.Net.Sfe.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConnectionIndexSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_RemoteFiles_ConnectionId_NameKey_FullName",
                schema: "SFE",
                table: "RemoteFiles",
                columns: new[] { "ConnectionId", "NameKey", "FullName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RemoteFiles_ConnectionId_NameKey_FullName",
                schema: "SFE",
                table: "RemoteFiles");
        }
    }
}
