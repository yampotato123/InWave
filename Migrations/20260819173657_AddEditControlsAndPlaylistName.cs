using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InWave.Migrations
{
    /// <inheritdoc />
    public partial class AddEditControlsAndPlaylistName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlaylistName",
                table: "Photos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurvePoints",
                table: "PhotoEdits",
                type: "TEXT",
                nullable: false,
                defaultValue: "0,0.25,0.5,0.75,1");   // 對角線 = 不改變;空字串不是合法曲線

            migrationBuilder.AddColumn<int>(
                name: "Sharpness",
                table: "PhotoEdits",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Softness",
                table: "PhotoEdits",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Temperature",
                table: "PhotoEdits",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlaylistName",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "CurvePoints",
                table: "PhotoEdits");

            migrationBuilder.DropColumn(
                name: "Sharpness",
                table: "PhotoEdits");

            migrationBuilder.DropColumn(
                name: "Softness",
                table: "PhotoEdits");

            migrationBuilder.DropColumn(
                name: "Temperature",
                table: "PhotoEdits");
        }
    }
}
