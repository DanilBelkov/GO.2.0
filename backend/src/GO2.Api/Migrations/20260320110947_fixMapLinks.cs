using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GO2.Api.Migrations
{
    /// <inheritdoc />
    public partial class fixMapLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Maps_MapVersions_ActiveVersionId",
                table: "Maps");

            migrationBuilder.DropIndex(
                name: "IX_Maps_ActiveVersionId",
                table: "Maps");

            migrationBuilder.DropColumn(
                name: "ActiveVersionId",
                table: "Maps");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveVersionId",
                table: "Maps",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Maps_ActiveVersionId",
                table: "Maps",
                column: "ActiveVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Maps_MapVersions_ActiveVersionId",
                table: "Maps",
                column: "ActiveVersionId",
                principalTable: "MapVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
