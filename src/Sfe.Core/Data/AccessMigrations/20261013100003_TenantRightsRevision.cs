using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sfe.Core.Data.AccessMigrations
{
    /// <inheritdoc />
    public partial class TenantRightsRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RightsRevision",
                schema: "SFE",
                table: "Tenants",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RightsRevision",
                schema: "SFE",
                table: "Tenants");
        }
    }
}
