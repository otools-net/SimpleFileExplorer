using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sfe.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "SFE");

            migrationBuilder.CreateTable(
                name: "Directories",
                schema: "SFE",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    LastWriteTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    LastAccessTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    RegisterTime = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Directories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Directories_Directories_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "SFE",
                        principalTable: "Directories",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Files",
                schema: "SFE",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DirectoryId = table.Column<int>(type: "int", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Extension = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sha1 = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    LastWriteTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    LastAccessTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    RegisterTime = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Files", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Files_Directories_DirectoryId",
                        column: x => x.DirectoryId,
                        principalSchema: "SFE",
                        principalTable: "Directories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "IX_Directories_FullName",
                schema: "SFE",
                table: "Directories",
                column: "FullName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Directories_Name",
                schema: "SFE",
                table: "Directories",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Directories_ParentId",
                schema: "SFE",
                table: "Directories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Files_DirectoryId",
                schema: "SFE",
                table: "Files",
                column: "DirectoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Files_FullName",
                schema: "SFE",
                table: "Files",
                column: "FullName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Files_Name",
                schema: "SFE",
                table: "Files",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Files_Sha1",
                schema: "SFE",
                table: "Files",
                column: "Sha1");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tags",
                schema: "SFE");

            migrationBuilder.DropTable(
                name: "Files",
                schema: "SFE");

            migrationBuilder.DropTable(
                name: "Directories",
                schema: "SFE");
        }
    }
}
