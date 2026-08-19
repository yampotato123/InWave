using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InWave.Migrations
{
    /// <inheritdoc />
    public partial class AddMoodIsAiFilled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAiFilled",
                table: "MoodProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAiFilled",
                table: "MoodProfiles");
        }
    }
}
