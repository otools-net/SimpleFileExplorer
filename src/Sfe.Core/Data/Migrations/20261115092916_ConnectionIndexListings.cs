using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Otools.Net.Sfe.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConnectionIndexListings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NameKey",
                schema: "SFE",
                table: "RemoteFiles",
                type: "varchar(400)",
                unicode: false,
                maxLength: 400,
                nullable: true,
                collation: "Latin1_General_BIN2");

            migrationBuilder.AddColumn<string>(
                name: "NameKey",
                schema: "SFE",
                table: "RemoteDirectories",
                type: "varchar(400)",
                unicode: false,
                maxLength: 400,
                nullable: true,
                collation: "Latin1_General_BIN2");

            migrationBuilder.CreateIndex(
                name: "IX_RemoteFiles_ConnectionId_DirectoryId_Extension_NameKey_FullName",
                schema: "SFE",
                table: "RemoteFiles",
                columns: new[] { "ConnectionId", "DirectoryId", "Extension", "NameKey", "FullName" });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteFiles_ConnectionId_DirectoryId_LastWriteTime_NameKey_FullName",
                schema: "SFE",
                table: "RemoteFiles",
                columns: new[] { "ConnectionId", "DirectoryId", "LastWriteTime", "NameKey", "FullName" });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteFiles_ConnectionId_DirectoryId_NameKey_FullName_Removed",
                schema: "SFE",
                table: "RemoteFiles",
                columns: new[] { "ConnectionId", "DirectoryId", "NameKey", "FullName", "Removed" });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteFiles_ConnectionId_DirectoryId_Size_NameKey_FullName",
                schema: "SFE",
                table: "RemoteFiles",
                columns: new[] { "ConnectionId", "DirectoryId", "Size", "NameKey", "FullName" });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteDirectories_ConnectionId_ParentId_NameKey_FullName_Removed",
                schema: "SFE",
                table: "RemoteDirectories",
                columns: new[] { "ConnectionId", "ParentId", "NameKey", "FullName", "Removed" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RemoteFiles_ConnectionId_DirectoryId_Extension_NameKey_FullName",
                schema: "SFE",
                table: "RemoteFiles");

            migrationBuilder.DropIndex(
                name: "IX_RemoteFiles_ConnectionId_DirectoryId_LastWriteTime_NameKey_FullName",
                schema: "SFE",
                table: "RemoteFiles");

            migrationBuilder.DropIndex(
                name: "IX_RemoteFiles_ConnectionId_DirectoryId_NameKey_FullName_Removed",
                schema: "SFE",
                table: "RemoteFiles");

            migrationBuilder.DropIndex(
                name: "IX_RemoteFiles_ConnectionId_DirectoryId_Size_NameKey_FullName",
                schema: "SFE",
                table: "RemoteFiles");

            migrationBuilder.DropIndex(
                name: "IX_RemoteDirectories_ConnectionId_ParentId_NameKey_FullName_Removed",
                schema: "SFE",
                table: "RemoteDirectories");

            migrationBuilder.DropColumn(
                name: "NameKey",
                schema: "SFE",
                table: "RemoteFiles");

            migrationBuilder.DropColumn(
                name: "NameKey",
                schema: "SFE",
                table: "RemoteDirectories");
        }
    }
}
