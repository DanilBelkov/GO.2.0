using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GO2.Api.Migrations
{
    /// <inheritdoc />
    public partial class add_ocd_symbol_styles_and_icons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IconDataUrl",
                table: "TerrainObjectTypes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StyleJson",
                table: "TerrainObjectTypes",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IconDataUrl",
                table: "TerrainObjectTypes");

            migrationBuilder.DropColumn(
                name: "StyleJson",
                table: "TerrainObjectTypes");
        }
    }
}
