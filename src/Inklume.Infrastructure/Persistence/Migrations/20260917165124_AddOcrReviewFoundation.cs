using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inklume.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOcrReviewFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReviewStatus",
                table: "TextRegions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedAt",
                table: "TextRegions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedText",
                table: "TextRegions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UserModifiedAt",
                table: "TextRegions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TextRegions_ReviewStatus_Valid",
                table: "TextRegions",
                sql: "\"ReviewStatus\" IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TextRegions_ReviewStatus_Valid",
                table: "TextRegions");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "TextRegions");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "TextRegions");

            migrationBuilder.DropColumn(
                name: "ReviewedText",
                table: "TextRegions");

            migrationBuilder.DropColumn(
                name: "UserModifiedAt",
                table: "TextRegions");
        }
    }
}
