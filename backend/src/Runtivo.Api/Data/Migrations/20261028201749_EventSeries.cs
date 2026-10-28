using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Runtivo.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class EventSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "OccurrenceLocalDate",
                table: "Events",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SeriesId",
                table: "Events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Series",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    MeetingPointLabel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Visibility = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Weekday = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LocalStartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    GroupsJson = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Series", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Series_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_SeriesId_OccurrenceLocalDate",
                table: "Events",
                columns: new[] { "SeriesId", "OccurrenceLocalDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Series_ClubId_Enabled",
                table: "Series",
                columns: new[] { "ClubId", "Enabled" });

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Series_SeriesId",
                table: "Events",
                column: "SeriesId",
                principalTable: "Series",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_Series_SeriesId",
                table: "Events");

            migrationBuilder.DropTable(
                name: "Series");

            migrationBuilder.DropIndex(
                name: "IX_Events_SeriesId_OccurrenceLocalDate",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "OccurrenceLocalDate",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "Events");
        }
    }
}
