using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Otools.Net.Sfe.Core.Data.AccessMigrations
{
    /// <inheritdoc />
    public partial class ConnectionFolderRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConnectionFolderRules",
                schema: "SFE",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConnectionId = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Path = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Level = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ItemId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectionFolderRules", x => x.Id);
                    table.CheckConstraint("CK_ConnectionFolderRules_Principal", "(GroupId IS NULL AND UserId IS NOT NULL) OR (GroupId IS NOT NULL AND UserId IS NULL)");
                    table.ForeignKey(
                        name: "FK_ConnectionFolderRules_Connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "SFE",
                        principalTable: "Connections",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ConnectionFolderRules_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "SFE",
                        principalTable: "Groups",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ConnectionFolderRules_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "SFE",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConnectionFolderRules_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "SFE",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConnectionFolderRules_ConnectionId",
                schema: "SFE",
                table: "ConnectionFolderRules",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ConnectionFolderRules_GroupId",
                schema: "SFE",
                table: "ConnectionFolderRules",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ConnectionFolderRules_TenantId_ConnectionId",
                schema: "SFE",
                table: "ConnectionFolderRules",
                columns: new[] { "TenantId", "ConnectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConnectionFolderRules_UserId",
                schema: "SFE",
                table: "ConnectionFolderRules",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConnectionFolderRules",
                schema: "SFE");
        }
    }
}
