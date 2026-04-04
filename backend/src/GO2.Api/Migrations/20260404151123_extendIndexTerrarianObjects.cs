using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GO2.Api.Migrations
{
    /// <inheritdoc />
    public partial class extendIndexTerrarianObjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TerrainObjectTypes_OwnerUserId_Name",
                table: "TerrainObjectTypes");

            migrationBuilder.CreateIndex(
                name: "IX_TerrainObjectTypes_OwnerUserId_Name_SymbolCode",
                table: "TerrainObjectTypes",
                columns: new[] { "OwnerUserId", "Name", "SymbolCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TerrainObjectTypes_OwnerUserId_Name_SymbolCode",
                table: "TerrainObjectTypes");

            migrationBuilder.CreateIndex(
                name: "IX_TerrainObjectTypes_OwnerUserId_Name",
                table: "TerrainObjectTypes",
                columns: new[] { "OwnerUserId", "Name" },
                unique: true);
        }
    }
}
