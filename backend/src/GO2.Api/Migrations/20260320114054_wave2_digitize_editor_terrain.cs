using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GO2.Api.Migrations
{
    /// <inheritdoc />
    public partial class wave2_digitize_editor_terrain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DigitizationJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MapId = table.Column<Guid>(type: "uuid", nullable: false),
                    MapVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Progress = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: false),
                    MacroF1 = table.Column<decimal>(type: "numeric", nullable: true),
                    IoU = table.Column<decimal>(type: "numeric", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DigitizationJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DigitizationJobs_MapVersions_MapVersionId",
                        column: x => x.MapVersionId,
                        principalTable: "MapVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DigitizationJobs_Maps_MapId",
                        column: x => x.MapId,
                        principalTable: "Maps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TerrainObjectTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Color = table.Column<string>(type: "text", nullable: false),
                    Icon = table.Column<string>(type: "text", nullable: false),
                    Traversability = table.Column<decimal>(type: "numeric", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerrainObjectTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TerrainObjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MapId = table.Column<Guid>(type: "uuid", nullable: false),
                    MapVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TerrainClass = table.Column<int>(type: "integer", nullable: false),
                    TerrainObjectTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    GeometryKind = table.Column<int>(type: "integer", nullable: false),
                    GeometryJson = table.Column<string>(type: "text", nullable: false),
                    Traversability = table.Column<decimal>(type: "numeric", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerrainObjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TerrainObjects_MapVersions_MapVersionId",
                        column: x => x.MapVersionId,
                        principalTable: "MapVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TerrainObjects_Maps_MapId",
                        column: x => x.MapId,
                        principalTable: "Maps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TerrainObjects_TerrainObjectTypes_TerrainObjectTypeId",
                        column: x => x.TerrainObjectTypeId,
                        principalTable: "TerrainObjectTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DigitizationJobs_MapId_OwnerUserId_CreatedAtUtc",
                table: "DigitizationJobs",
                columns: new[] { "MapId", "OwnerUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DigitizationJobs_MapVersionId",
                table: "DigitizationJobs",
                column: "MapVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_TerrainObjects_MapId",
                table: "TerrainObjects",
                column: "MapId");

            migrationBuilder.CreateIndex(
                name: "IX_TerrainObjects_MapVersionId",
                table: "TerrainObjects",
                column: "MapVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_TerrainObjects_TerrainObjectTypeId",
                table: "TerrainObjects",
                column: "TerrainObjectTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TerrainObjectTypes_OwnerUserId_Name",
                table: "TerrainObjectTypes",
                columns: new[] { "OwnerUserId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DigitizationJobs");

            migrationBuilder.DropTable(
                name: "TerrainObjects");

            migrationBuilder.DropTable(
                name: "TerrainObjectTypes");
        }
    }
}
