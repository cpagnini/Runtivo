using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Runtivo.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class EventsGroupsRsvps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClubId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MeetingPointLabel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Visibility = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Events_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaceGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DistanceMeters = table.Column<int>(type: "integer", nullable: true),
                    PaceMinSecPerKm = table.Column<int>(type: "integer", nullable: true),
                    PaceMaxSecPerKm = table.Column<int>(type: "integer", nullable: true),
                    LeaderUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Capacity = table.Column<int>(type: "integer", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaceGroups", x => x.Id);
                    table.CheckConstraint("CK_PaceGroups_Capacity", "\"Capacity\" IS NULL OR \"Capacity\" > 0");
                    table.CheckConstraint("CK_PaceGroups_Distance", "\"DistanceMeters\" IS NULL OR \"DistanceMeters\" > 0");
                    table.CheckConstraint("CK_PaceGroups_Pace", "(\"PaceMinSecPerKm\" IS NULL OR \"PaceMinSecPerKm\" > 0) AND (\"PaceMaxSecPerKm\" IS NULL OR \"PaceMaxSecPerKm\" > 0) AND (\"PaceMinSecPerKm\" IS NULL OR \"PaceMaxSecPerKm\" IS NULL OR \"PaceMinSecPerKm\" <= \"PaceMaxSecPerKm\")");
                    table.ForeignKey(
                        name: "FK_PaceGroups_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Rsvps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaceGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rsvps", x => x.Id);
                    table.CheckConstraint("CK_Rsvps_GroupWhenGoing", "(\"Status\" = 'Going' AND \"PaceGroupId\" IS NOT NULL) OR (\"Status\" <> 'Going' AND \"PaceGroupId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_Rsvps_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Rsvps_PaceGroups_PaceGroupId",
                        column: x => x.PaceGroupId,
                        principalTable: "PaceGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rsvps_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_ClubId_StartsAtUtc",
                table: "Events",
                columns: new[] { "ClubId", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PaceGroups_EventId",
                table: "PaceGroups",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_Rsvps_EventId_UserId",
                table: "Rsvps",
                columns: new[] { "EventId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rsvps_PaceGroupId",
                table: "Rsvps",
                column: "PaceGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Rsvps_UserId",
                table: "Rsvps",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Rsvps");

            migrationBuilder.DropTable(
                name: "PaceGroups");

            migrationBuilder.DropTable(
                name: "Events");
        }
    }
}
