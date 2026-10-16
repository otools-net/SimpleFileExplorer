using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sfe.Core.Data.AccessMigrations
{
    /// <inheritdoc />
    public partial class FolderRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FolderRules",
                schema: "SFE",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Path = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Level = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FolderRules", x => x.Id);
                    table.CheckConstraint("CK_FolderRules_Principal", "(GroupId IS NULL AND UserId IS NOT NULL) OR (GroupId IS NOT NULL AND UserId IS NULL)");
                    table.ForeignKey(
                        name: "FK_FolderRules_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "SFE",
                        principalTable: "Groups",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FolderRules_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "SFE",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FolderRules_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "SFE",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FolderRules_GroupId",
                schema: "SFE",
                table: "FolderRules",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_FolderRules_TenantId",
                schema: "SFE",
                table: "FolderRules",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FolderRules_UserId",
                schema: "SFE",
                table: "FolderRules",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FolderRules",
                schema: "SFE");
        }
    }
}
