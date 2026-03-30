using GO2.Api.Services;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GO2.Api.Migrations
{
    /// <inheritdoc />
    public partial class add_terrain_symbol_classes_and_catalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "TerrainObjectTypes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Icon",
                table: "TerrainObjectTypes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "TerrainObjectTypes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Color",
                table: "TerrainObjectTypes",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "SymbolCode",
                table: "TerrainObjectTypes",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SymbolStyle",
                table: "TerrainObjectTypes",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TerrainClass",
                table: "TerrainObjectTypes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_TerrainObjectTypes_OwnerUserId_SymbolCode",
                table: "TerrainObjectTypes",
                columns: new[] { "OwnerUserId", "SymbolCode" },
                unique: true,
                filter: "\"SymbolCode\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_TerrainObjectTypes_SymbolCode",
                table: "TerrainObjectTypes",
                column: "SymbolCode",
                unique: true,
                filter: "\"IsSystem\" = true AND \"SymbolCode\" <> ''");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TerrainObjectType_Traversability_0_100",
                table: "TerrainObjectTypes",
                sql: "\"Traversability\" >= 0 AND \"Traversability\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TerrainObject_Traversability_0_100",
                table: "TerrainObjects",
                sql: "\"Traversability\" >= 0 AND \"Traversability\" <= 100");

            migrationBuilder.Sql("""
                UPDATE "TerrainObjectTypes"
                SET "Traversability" = LEAST(100, GREATEST(0, ROUND("Traversability" * 10, 2)));
                """);
            migrationBuilder.Sql("""
                UPDATE "TerrainObjects"
                SET "Traversability" = LEAST(100, GREATEST(0, ROUND("Traversability" * 10, 2)));
                """);

            migrationBuilder.Sql("""
                UPDATE "TerrainObjects"
                SET "TerrainObjectTypeId" = NULL
                WHERE "TerrainObjectTypeId" IN (
                    SELECT "Id" FROM "TerrainObjectTypes" WHERE "IsSystem" = true
                );
                DELETE FROM "TerrainObjectTypes" WHERE "IsSystem" = true;
                """);

            foreach (var seed in TerrainSymbolCatalog.All)
            {
                migrationBuilder.InsertData(
                    table: "TerrainObjectTypes",
                    columns:
                    [
                        "Id",
                        "OwnerUserId",
                        "TerrainClass",
                        "SymbolCode",
                        "SymbolStyle",
                        "Name",
                        "Color",
                        "Icon",
                        "Traversability",
                        "Comment",
                        "IsSystem",
                        "CreatedAtUtc"
                    ],
                    values:
                    [
                        Guid.NewGuid(),
                        null,
                        (int)seed.TerrainClass,
                        seed.SymbolCode,
                        seed.SymbolStyle,
                        seed.Name,
                        seed.Color,
                        seed.Icon,
                        seed.Traversability,
                        seed.Comment,
                        true,
                        DateTime.UtcNow
                    ]);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TerrainObjectTypes_OwnerUserId_SymbolCode",
                table: "TerrainObjectTypes");

            migrationBuilder.DropIndex(
                name: "IX_TerrainObjectTypes_SymbolCode",
                table: "TerrainObjectTypes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TerrainObjectType_Traversability_0_100",
                table: "TerrainObjectTypes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TerrainObject_Traversability_0_100",
                table: "TerrainObjects");

            migrationBuilder.DropColumn(
                name: "SymbolCode",
                table: "TerrainObjectTypes");

            migrationBuilder.DropColumn(
                name: "SymbolStyle",
                table: "TerrainObjectTypes");

            migrationBuilder.DropColumn(
                name: "TerrainClass",
                table: "TerrainObjectTypes");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "TerrainObjectTypes",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<string>(
                name: "Icon",
                table: "TerrainObjectTypes",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "TerrainObjectTypes",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "Color",
                table: "TerrainObjectTypes",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.Sql("""
                UPDATE "TerrainObjectTypes"
                SET "Traversability" = ROUND("Traversability" / 10, 2);
                UPDATE "TerrainObjects"
                SET "Traversability" = ROUND("Traversability" / 10, 2);
                """);
        }
    }
}
