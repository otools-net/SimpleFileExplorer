using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sfe.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Tagging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TagDefinitions",
                schema: "SFE",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Shorthand = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Color = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    IsCategory = table.Column<bool>(type: "bit", nullable: false),
                    IsHidden = table.Column<bool>(type: "bit", nullable: false),
                    DisambiguationId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TagDefinitions_TagDefinitions_DisambiguationId",
                        column: x => x.DisambiguationId,
                        principalSchema: "SFE",
                        principalTable: "TagDefinitions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FileTags",
                schema: "SFE",
                columns: table => new
                {
                    FileId = table.Column<int>(type: "int", nullable: false),
                    TagId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileTags", x => new { x.FileId, x.TagId });
                    table.ForeignKey(
                        name: "FK_FileTags_Files_FileId",
                        column: x => x.FileId,
                        principalSchema: "SFE",
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FileTags_TagDefinitions_TagId",
                        column: x => x.TagId,
                        principalSchema: "SFE",
                        principalTable: "TagDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TagAliases",
                schema: "SFE",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TagId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TagAliases_TagDefinitions_TagId",
                        column: x => x.TagId,
                        principalSchema: "SFE",
                        principalTable: "TagDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TagParents",
                schema: "SFE",
                columns: table => new
                {
                    ParentId = table.Column<int>(type: "int", nullable: false),
                    ChildId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagParents", x => new { x.ParentId, x.ChildId });
                    table.ForeignKey(
                        name: "FK_TagParents_TagDefinitions_ChildId",
                        column: x => x.ChildId,
                        principalSchema: "SFE",
                        principalTable: "TagDefinitions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TagParents_TagDefinitions_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "SFE",
                        principalTable: "TagDefinitions",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                schema: "SFE",
                table: "TagDefinitions",
                columns: new[] { "Id", "Color", "DisambiguationId", "IsCategory", "IsHidden", "Name", "Shorthand" },
                values: new object[,]
                {
                    { 1, "red", null, false, true, "Archived", null },
                    { 2, "yellow", null, false, false, "Favorite", null },
                    { 3, null, null, true, false, "Meta Tags", null }
                });

            migrationBuilder.InsertData(
                schema: "SFE",
                table: "TagAliases",
                columns: new[] { "Id", "Name", "TagId" },
                values: new object[,]
                {
                    { 1, "Archive", 1 },
                    { 2, "Favorited", 2 },
                    { 3, "Favorites", 2 },
                    { 4, "Meta", 3 },
                    { 5, "Meta Tag", 3 }
                });

            migrationBuilder.InsertData(
                schema: "SFE",
                table: "TagParents",
                columns: new[] { "ChildId", "ParentId" },
                values: new object[,]
                {
                    { 1, 3 },
                    { 2, 3 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileTags_TagId",
                schema: "SFE",
                table: "FileTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_TagAliases_Name",
                schema: "SFE",
                table: "TagAliases",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_TagAliases_TagId",
                schema: "SFE",
                table: "TagAliases",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_TagDefinitions_DisambiguationId",
                schema: "SFE",
                table: "TagDefinitions",
                column: "DisambiguationId");

            migrationBuilder.CreateIndex(
                name: "IX_TagDefinitions_Name",
                schema: "SFE",
                table: "TagDefinitions",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_TagParents_ChildId",
                schema: "SFE",
                table: "TagParents",
                column: "ChildId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileTags",
                schema: "SFE");

            migrationBuilder.DropTable(
                name: "TagAliases",
                schema: "SFE");

            migrationBuilder.DropTable(
                name: "TagParents",
                schema: "SFE");

            migrationBuilder.DropTable(
                name: "TagDefinitions",
                schema: "SFE");
        }
    }
}
