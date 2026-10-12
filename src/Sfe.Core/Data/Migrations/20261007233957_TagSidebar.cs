using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sfe.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class TagSidebar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The legacy key/value table (SFE.Tags) was never used by this application: its rows are discarded.
            migrationBuilder.DropTable(
                name: "Tags",
                schema: "SFE");

            migrationBuilder.CreateTable(
                name: "TagActivity",
                schema: "SFE",
                columns: table => new
                {
                    TagId = table.Column<int>(type: "int", nullable: false),
                    LastSearchedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastViewedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagActivity", x => x.TagId);
                    table.ForeignKey(
                        name: "FK_TagActivity_TagDefinitions_TagId",
                        column: x => x.TagId,
                        principalSchema: "SFE",
                        principalTable: "TagDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TagActivity_LastSearchedAt",
                schema: "SFE",
                table: "TagActivity",
                column: "LastSearchedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TagActivity_LastViewedAt",
                schema: "SFE",
                table: "TagActivity",
                column: "LastViewedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TagActivity",
                schema: "SFE");

            migrationBuilder.CreateTable(
                name: "Tags",
                schema: "SFE",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileId = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tags_Files_FileId",
                        column: x => x.FileId,
                        principalSchema: "SFE",
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tags_FileId",
                schema: "SFE",
                table: "Tags",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Key",
                schema: "SFE",
                table: "Tags",
                column: "Key");
        }
    }
}
