using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sfe.Core.Data.AccessMigrations
{
    /// <inheritdoc />
    public partial class AccessTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessTokens",
                schema: "SFE",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Lookup = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Hash = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                    Prefix = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    Rights = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    Kind = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Expires = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastUsed = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastUsedFrom = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    Revoked = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessTokens_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "SFE",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccessTokens_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "SFE",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessTokens_Lookup",
                schema: "SFE",
                table: "AccessTokens",
                column: "Lookup",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessTokens_TenantId",
                schema: "SFE",
                table: "AccessTokens",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessTokens_UserId",
                schema: "SFE",
                table: "AccessTokens",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessTokens",
                schema: "SFE");
        }
    }
}
