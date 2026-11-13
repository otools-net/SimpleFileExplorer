using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sfe.Core.Data.AccessMigrations
{
    /// <inheritdoc />
    public partial class Connections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Connections",
                schema: "SFE",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Level = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    OwnerId = table.Column<int>(type: "int", nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    AllTenants = table.Column<bool>(type: "bit", nullable: false),
                    RootPath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Credentials = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Account = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    AccountId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    State = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    Disabled = table.Column<bool>(type: "bit", nullable: false),
                    ReadOnly = table.Column<bool>(type: "bit", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Connections", x => x.Id);
                    table.CheckConstraint("CK_Connections_Level", "(Level = 'Account' AND OwnerId IS NOT NULL AND TenantId IS NULL) OR (Level = 'Library' AND OwnerId IS NULL AND TenantId IS NOT NULL) OR (Level = 'Server' AND OwnerId IS NULL AND TenantId IS NULL)");
                    table.ForeignKey(
                        name: "FK_Connections_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "SFE",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Connections_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "SFE",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConnectionGroups",
                schema: "SFE",
                columns: table => new
                {
                    ConnectionId = table.Column<int>(type: "int", nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    Level = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectionGroups", x => new { x.ConnectionId, x.GroupId });
                    table.ForeignKey(
                        name: "FK_ConnectionGroups_Connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "SFE",
                        principalTable: "Connections",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ConnectionGroups_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "SFE",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConnectionTenants",
                schema: "SFE",
                columns: table => new
                {
                    ConnectionId = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectionTenants", x => new { x.ConnectionId, x.TenantId });
                    table.ForeignKey(
                        name: "FK_ConnectionTenants_Connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "SFE",
                        principalTable: "Connections",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ConnectionTenants_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "SFE",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConnectionGroups_GroupId",
                schema: "SFE",
                table: "ConnectionGroups",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Connections_OwnerId",
                schema: "SFE",
                table: "Connections",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Connections_TenantId",
                schema: "SFE",
                table: "Connections",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ConnectionTenants_TenantId",
                schema: "SFE",
                table: "ConnectionTenants",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConnectionGroups",
                schema: "SFE");

            migrationBuilder.DropTable(
                name: "ConnectionTenants",
                schema: "SFE");

            migrationBuilder.DropTable(
                name: "Connections",
                schema: "SFE");
        }
    }
}
