using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sassoir.Api.Migrations
{
    /// <inheritdoc />
    public partial class DjAccessToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "dj_access_token",
                table: "events",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("create extension if not exists pgcrypto;");
            migrationBuilder.Sql("""
                update events
                set dj_access_token = replace(replace(trim(trailing '=' from encode(gen_random_bytes(24), 'base64')), '+', '-'), '/', '_')
                where dj_access_token = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "dj_access_token",
                table: "events");
        }
    }
}
