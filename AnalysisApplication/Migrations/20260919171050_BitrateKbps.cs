using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnalysisApplication.Migrations
{
    /// <inheritdoc />
    public partial class BitrateKbps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FilleName",
                table: "VideoItems",
                newName: "FileName");

            migrationBuilder.AddColumn<int>(
                name: "BitrateKbps",
                table: "VideoItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BitrateKbps",
                table: "VideoItems");

            migrationBuilder.RenameColumn(
                name: "FileName",
                table: "VideoItems",
                newName: "FilleName");
        }
    }
}
