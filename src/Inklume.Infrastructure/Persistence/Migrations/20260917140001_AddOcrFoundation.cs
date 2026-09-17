using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inklume.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOcrFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Origin",
                table: "TextRegions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "OcrRecognitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TextRegionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    RecognitionConfidence = table.Column<double>(type: "REAL", nullable: false),
                    DetectionConfidence = table.Column<double>(type: "REAL", nullable: true),
                    EngineName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EngineVersion = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DetectionModel = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RecognitionModel = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModelProfile = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OcrRecognitions", x => x.Id);
                    table.CheckConstraint("CK_OcrRecognitions_DetectionConfidence_Range", "\"DetectionConfidence\" IS NULL OR (\"DetectionConfidence\" >= 0.0 AND \"DetectionConfidence\" <= 1.0)");
                    table.CheckConstraint("CK_OcrRecognitions_RecognitionConfidence_Range", "\"RecognitionConfidence\" >= 0.0 AND \"RecognitionConfidence\" <= 1.0");
                    table.ForeignKey(
                        name: "FK_OcrRecognitions_TextRegions_TextRegionId",
                        column: x => x.TextRegionId,
                        principalTable: "TextRegions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OcrRecognitions_TextRegionId",
                table: "OcrRecognitions",
                column: "TextRegionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OcrRecognitions");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "TextRegions");
        }
    }
}
