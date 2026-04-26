using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vod2Tube.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelQueueState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastQueueCheckAtUTC",
                table: "Channels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastQueuedVodId",
                table: "Channels",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastQueueCheckAtUTC",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "LastQueuedVodId",
                table: "Channels");
        }
    }
}
