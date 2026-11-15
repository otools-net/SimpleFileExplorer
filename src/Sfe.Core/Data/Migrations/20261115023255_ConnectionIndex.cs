using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Otools.Net.Sfe.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConnectionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RemoteDirectories",
                schema: "SFE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConnectionId = table.Column<int>(type: "int", nullable: false),
                    ParentId = table.Column<long>(type: "bigint", nullable: true),
                    ProviderId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    DisplayPath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    LastWriteTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Listed = table.Column<bool>(type: "bit", nullable: false),
                    Pending = table.Column<bool>(type: "bit", nullable: false),
                    Pass = table.Column<int>(type: "int", nullable: false),
                    Removed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteDirectories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemoteDirectories_RemoteDirectories_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "SFE",
                        principalTable: "RemoteDirectories",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RemoteIndexes",
                schema: "SFE",
                columns: table => new
                {
                    ConnectionId = table.Column<int>(type: "int", nullable: false),
                    RootPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AccountKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    Phase = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Pass = table.Column<int>(type: "int", nullable: false),
                    ChangeCursor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CursorTaken = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WalkFolderId = table.Column<long>(type: "bigint", nullable: true),
                    WalkCursor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ListingStarted = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSync = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Files = table.Column<long>(type: "bigint", nullable: false),
                    Folders = table.Column<long>(type: "bigint", nullable: false),
                    TooLong = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteIndexes", x => x.ConnectionId);
                });

            migrationBuilder.CreateTable(
                name: "RemoteFiles",
                schema: "SFE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConnectionId = table.Column<int>(type: "int", nullable: false),
                    DirectoryId = table.Column<long>(type: "bigint", nullable: false),
                    ProviderId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    DisplayPath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Extension = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    LastWriteTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ContentHash = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    Downloadable = table.Column<bool>(type: "bit", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true),
                    Duration = table.Column<double>(type: "float", nullable: true),
                    Pixels = table.Column<long>(type: "bigint", nullable: true, computedColumnSql: "CAST([Width] AS bigint) * [Height]", stored: false),
                    Pass = table.Column<int>(type: "int", nullable: false),
                    Removed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemoteFiles_RemoteDirectories_DirectoryId",
                        column: x => x.DirectoryId,
                        principalSchema: "SFE",
                        principalTable: "RemoteDirectories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RemoteFileTags",
                schema: "SFE",
                columns: table => new
                {
                    FileId = table.Column<long>(type: "bigint", nullable: false),
                    TagId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteFileTags", x => new { x.FileId, x.TagId });
                    table.ForeignKey(
                        name: "FK_RemoteFileTags_RemoteFiles_FileId",
                        column: x => x.FileId,
                        principalSchema: "SFE",
                        principalTable: "RemoteFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RemoteFileTags_TagDefinitions_TagId",
                        column: x => x.TagId,
                        principalSchema: "SFE",
                        principalTable: "TagDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteDirectories_ConnectionId_FullName",
                schema: "SFE",
                table: "RemoteDirectories",
                columns: new[] { "ConnectionId", "FullName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemoteDirectories_ConnectionId_Pending",
                schema: "SFE",
                table: "RemoteDirectories",
                columns: new[] { "ConnectionId", "Pending" });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteDirectories_ConnectionId_ProviderId",
                schema: "SFE",
                table: "RemoteDirectories",
                columns: new[] { "ConnectionId", "ProviderId" });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteDirectories_ConnectionId_Removed",
                schema: "SFE",
                table: "RemoteDirectories",
                columns: new[] { "ConnectionId", "Removed" });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteDirectories_ParentId",
                schema: "SFE",
                table: "RemoteDirectories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_RemoteFiles_ConnectionId_DirectoryId",
                schema: "SFE",
                table: "RemoteFiles",
                columns: new[] { "ConnectionId", "DirectoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteFiles_ConnectionId_Duration",
                schema: "SFE",
                table: "RemoteFiles",
                columns: new[] { "ConnectionId", "Duration" });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteFiles_ConnectionId_Extension",
                schema: "SFE",
                table: "RemoteFiles",
                columns: new[] { "ConnectionId", "Extension" });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteFiles_ConnectionId_FullName",
                schema: "SFE",
                table: "RemoteFiles",
                columns: new[] { "ConnectionId", "FullName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemoteFiles_ConnectionId_LastWriteTime",
                schema: "SFE",
                table: "RemoteFiles",
                columns: new[] { "ConnectionId", "LastWriteTime" });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteFiles_ConnectionId_Name",
                schema: "SFE",
                table: "RemoteFiles",
                columns: new[] { "ConnectionId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteFiles_ConnectionId_Pixels",
                schema: "SFE",
                table: "RemoteFiles",
                columns: new[] { "ConnectionId", "Pixels" });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteFiles_ConnectionId_ProviderId",
                schema: "SFE",
                table: "RemoteFiles",
                columns: new[] { "ConnectionId", "ProviderId" },
                unique: true,
                filter: "[ProviderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RemoteFiles_ConnectionId_Removed",
                schema: "SFE",
                table: "RemoteFiles",
                columns: new[] { "ConnectionId", "Removed" });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteFiles_ConnectionId_Size",
                schema: "SFE",
                table: "RemoteFiles",
                columns: new[] { "ConnectionId", "Size" });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteFiles_DirectoryId",
                schema: "SFE",
                table: "RemoteFiles",
                column: "DirectoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RemoteFileTags_TagId",
                schema: "SFE",
                table: "RemoteFileTags",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RemoteFileTags",
                schema: "SFE");

            migrationBuilder.DropTable(
                name: "RemoteIndexes",
                schema: "SFE");

            migrationBuilder.DropTable(
                name: "RemoteFiles",
                schema: "SFE");

            migrationBuilder.DropTable(
                name: "RemoteDirectories",
                schema: "SFE");
        }
    }
}
