using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sfe.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFolderDefaultTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultTags",
                schema: "SFE",
                table: "TrashEntries",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FolderDefaultTags",
                schema: "SFE",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Path = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    TagId = table.Column<int>(type: "int", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FolderDefaultTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FolderDefaultTags_TagDefinitions_TagId",
                        column: x => x.TagId,
                        principalSchema: "SFE",
                        principalTable: "TagDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FolderDefaultTags_Path_TagId",
                schema: "SFE",
                table: "FolderDefaultTags",
                columns: new[] { "Path", "TagId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FolderDefaultTags_TagId",
                schema: "SFE",
                table: "FolderDefaultTags",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FolderDefaultTags",
                schema: "SFE");

            migrationBuilder.DropColumn(
                name: "DefaultTags",
                schema: "SFE",
                table: "TrashEntries");
        }
    }
}
