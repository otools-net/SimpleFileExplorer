using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sfe.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class SearchSorts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Pixels",
                schema: "SFE",
                table: "Files",
                type: "bigint",
                nullable: true,
                computedColumnSql: "CAST([Width] AS bigint) * [Height]",
                stored: false);

            migrationBuilder.CreateIndex(
                name: "IX_Files_Duration",
                schema: "SFE",
                table: "Files",
                column: "Duration");

            migrationBuilder.CreateIndex(
                name: "IX_Files_Pixels",
                schema: "SFE",
                table: "Files",
                column: "Pixels");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Files_Duration",
                schema: "SFE",
                table: "Files");

            migrationBuilder.DropIndex(
                name: "IX_Files_Pixels",
                schema: "SFE",
                table: "Files");

            migrationBuilder.DropColumn(
                name: "Pixels",
                schema: "SFE",
                table: "Files");
        }
    }
}
