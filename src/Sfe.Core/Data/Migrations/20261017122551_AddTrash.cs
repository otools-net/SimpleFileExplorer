using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sfe.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrashEntries",
                schema: "SFE",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Path = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    IsFolder = table.Column<bool>(type: "bit", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: true),
                    Files = table.Column<int>(type: "int", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeletedById = table.Column<int>(type: "int", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Location = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Folder = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Rules = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrashEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrashTags",
                schema: "SFE",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TrashEntryId = table.Column<int>(type: "int", nullable: false),
                    Path = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TagId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrashTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrashTags_TagDefinitions_TagId",
                        column: x => x.TagId,
                        principalSchema: "SFE",
                        principalTable: "TagDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrashTags_TrashEntries_TrashEntryId",
                        column: x => x.TrashEntryId,
                        principalSchema: "SFE",
                        principalTable: "TrashEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrashEntries_DeletedAt",
                schema: "SFE",
                table: "TrashEntries",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TrashTags_TagId",
                schema: "SFE",
                table: "TrashTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_TrashTags_TrashEntryId",
                schema: "SFE",
                table: "TrashTags",
                column: "TrashEntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrashTags",
                schema: "SFE");

            migrationBuilder.DropTable(
                name: "TrashEntries",
                schema: "SFE");
        }
    }
}
