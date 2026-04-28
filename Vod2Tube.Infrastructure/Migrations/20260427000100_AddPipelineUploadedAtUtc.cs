using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vod2Tube.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineUploadedAtUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UploadedAtUTC",
                table: "Pipelines",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UploadedAtUTC",
                table: "Pipelines");
        }
    }
}
