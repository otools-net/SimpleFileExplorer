using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sfe.Core.Data.AccessMigrations
{
    /// <inheritdoc />
    public partial class ServerProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServerProperties",
                schema: "SFE",
                columns: table => new
                {
                    Key = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerProperties", x => x.Key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerProperties",
                schema: "SFE");
        }
    }
}
