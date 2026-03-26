using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vod2Tube.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Channels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChannelName = table.Column<string>(type: "TEXT", nullable: false),
                    AddedAtUTC = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Channels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Pipelines",
                columns: table => new
                {
                    VodId = table.Column<string>(type: "TEXT", nullable: false),
                    Stage = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    VodFilePath = table.Column<string>(type: "TEXT", nullable: false),
                    ChatTextFilePath = table.Column<string>(type: "TEXT", nullable: false),
                    ChatVideoFilePath = table.Column<string>(type: "TEXT", nullable: false),
                    FinalVideoFilePath = table.Column<string>(type: "TEXT", nullable: false),
                    YoutubeVideoId = table.Column<string>(type: "TEXT", nullable: false),
                    ResumableUploadUri = table.Column<string>(type: "TEXT", nullable: false),
                    Failed = table.Column<bool>(type: "INTEGER", nullable: false),
                    FailReason = table.Column<string>(type: "TEXT", nullable: false),
                    FailCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Paused = table.Column<bool>(type: "INTEGER", nullable: false),
                    PercentComplete = table.Column<double>(type: "REAL", nullable: true),
                    EstimatedMinutesRemaining = table.Column<double>(type: "REAL", nullable: true),
                    ArchivedVodPath = table.Column<string>(type: "TEXT", nullable: false),
                    ArchivedChatJsonPath = table.Column<string>(type: "TEXT", nullable: false),
                    ArchivedChatRenderPath = table.Column<string>(type: "TEXT", nullable: false),
                    ArchivedFinalVideoPath = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pipelines", x => x.VodId);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TwitchVods",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ChannelName = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUTC = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    AddedAtUTC = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwitchVods", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Settings_Key",
                table: "Settings",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Channels");

            migrationBuilder.DropTable(
                name: "Pipelines");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "TwitchVods");
        }
    }
}
