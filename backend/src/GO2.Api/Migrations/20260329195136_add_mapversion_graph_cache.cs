using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GO2.Api.Migrations
{
    /// <inheritdoc />
    public partial class add_mapversion_graph_cache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GraphJson",
                table: "MapVersions",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GraphJson",
                table: "MapVersions");
        }
    }
}
