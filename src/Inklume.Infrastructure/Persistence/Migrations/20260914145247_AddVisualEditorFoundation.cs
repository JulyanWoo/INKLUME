using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inklume.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVisualEditorFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PixelHeight",
                table: "Pages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PixelWidth",
                table: "Pages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TextRegions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GeometryKind = table.Column<int>(type: "INTEGER", nullable: false),
                    ReadingOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    ContainerType = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TextRegions", x => x.Id);
                    table.CheckConstraint("CK_TextRegions_ReadingOrder_Positive", "\"ReadingOrder\" > 0");
                    table.ForeignKey(
                        name: "FK_TextRegions_Pages_PageId",
                        column: x => x.PageId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TextRegionPoints",
                columns: table => new
                {
                    TextRegionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PointIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    X = table.Column<double>(type: "REAL", nullable: false),
                    Y = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TextRegionPoints", x => new { x.TextRegionId, x.PointIndex });
                    table.CheckConstraint("CK_TextRegionPoints_PointIndex_Nonnegative", "\"PointIndex\" >= 0");
                    table.ForeignKey(
                        name: "FK_TextRegionPoints_TextRegions_TextRegionId",
                        column: x => x.TextRegionId,
                        principalTable: "TextRegions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pages_Dimensions_Valid",
                table: "Pages",
                sql: "(\"PixelWidth\" IS NULL AND \"PixelHeight\" IS NULL) OR (\"PixelWidth\" > 0 AND \"PixelHeight\" > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_TextRegions_PageId_ReadingOrder",
                table: "TextRegions",
                columns: new[] { "PageId", "ReadingOrder" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TextRegionPoints");

            migrationBuilder.DropTable(
                name: "TextRegions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pages_Dimensions_Valid",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "PixelHeight",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "PixelWidth",
                table: "Pages");
        }
    }
}
