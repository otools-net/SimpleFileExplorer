using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sfe.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class FileTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MissingFiles",
                schema: "SFE",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Extension = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Sha1 = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    LastWriteTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    RegisterTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    MissingSince = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MissingFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MissingFileTags",
                schema: "SFE",
                columns: table => new
                {
                    MissingFileId = table.Column<int>(type: "int", nullable: false),
                    TagId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MissingFileTags", x => new { x.MissingFileId, x.TagId });
                    table.ForeignKey(
                        name: "FK_MissingFileTags_MissingFiles_MissingFileId",
                        column: x => x.MissingFileId,
                        principalSchema: "SFE",
                        principalTable: "MissingFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MissingFileTags_TagDefinitions_TagId",
                        column: x => x.TagId,
                        principalSchema: "SFE",
                        principalTable: "TagDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Files_Size_Sha1",
                schema: "SFE",
                table: "Files",
                columns: new[] { "Size", "Sha1" });

            migrationBuilder.CreateIndex(
                name: "IX_MissingFiles_FullName",
                schema: "SFE",
                table: "MissingFiles",
                column: "FullName");

            migrationBuilder.CreateIndex(
                name: "IX_MissingFiles_MissingSince",
                schema: "SFE",
                table: "MissingFiles",
                column: "MissingSince");

            migrationBuilder.CreateIndex(
                name: "IX_MissingFileTags_TagId",
                schema: "SFE",
                table: "MissingFileTags",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MissingFileTags",
                schema: "SFE");

            migrationBuilder.DropTable(
                name: "MissingFiles",
                schema: "SFE");

            migrationBuilder.DropIndex(
                name: "IX_Files_Size_Sha1",
                schema: "SFE",
                table: "Files");
        }
    }
}
