using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sassoir.Api.Migrations
{
    public partial class EventFeatureFlags : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "enable_floor_plan",
                table: "events",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "enable_table_companions",
                table: "events",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "enable_guest_messages",
                table: "events",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "enable_song_requests",
                table: "events",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "enable_floor_plan",
                table: "events");

            migrationBuilder.DropColumn(
                name: "enable_table_companions",
                table: "events");

            migrationBuilder.DropColumn(
                name: "enable_guest_messages",
                table: "events");

            migrationBuilder.DropColumn(
                name: "enable_song_requests",
                table: "events");
        }
    }
}
