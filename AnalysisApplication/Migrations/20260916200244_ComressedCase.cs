using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnalysisApplication.Migrations
{
    /// <inheritdoc />
    public partial class ComressedCase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCompressed",
                table: "VideoItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SourceVideoId",
                table: "VideoItems",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsCompressed",
                table: "VideoItems");

            migrationBuilder.DropColumn(
                name: "SourceVideoId",
                table: "VideoItems");
        }
    }
}
