using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vod2Tube.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddYouTubeAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "YouTubeAccountId",
                table: "Channels",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "YouTubeAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ClientSecretsJson = table.Column<string>(type: "TEXT", nullable: false),
                    AddedAtUTC = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ChannelTitle = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YouTubeAccounts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Channels_YouTubeAccountId",
                table: "Channels",
                column: "YouTubeAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_Channels_YouTubeAccounts_YouTubeAccountId",
                table: "Channels",
                column: "YouTubeAccountId",
                principalTable: "YouTubeAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Channels_YouTubeAccounts_YouTubeAccountId",
                table: "Channels");

            migrationBuilder.DropIndex(
                name: "IX_Channels_YouTubeAccountId",
                table: "Channels");

            migrationBuilder.DropTable(
                name: "YouTubeAccounts");

            migrationBuilder.DropColumn(
                name: "YouTubeAccountId",
                table: "Channels");
        }
    }
}
