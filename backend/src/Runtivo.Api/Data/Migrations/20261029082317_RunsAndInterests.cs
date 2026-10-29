using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Runtivo.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RunsAndInterests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Interests",
                columns: table => new
                {
                    FromUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Interests", x => new { x.FromUserId, x.ToUserId });
                    table.CheckConstraint("CK_Interests_NotSelf", "\"FromUserId\" <> \"ToUserId\"");
                    table.ForeignKey(
                        name: "FK_Interests_Users_FromUserId",
                        column: x => x.FromUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Interests_Users_ToUserId",
                        column: x => x.ToUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpontaneousRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DistanceMeters = table.Column<int>(type: "integer", nullable: true),
                    PaceMinSecPerKm = table.Column<int>(type: "integer", nullable: true),
                    PaceMaxSecPerKm = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CancelReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpontaneousRuns", x => x.Id);
                    table.CheckConstraint("CK_SpontaneousRuns_Pace", "(\"PaceMinSecPerKm\" IS NULL OR \"PaceMaxSecPerKm\" IS NULL OR \"PaceMinSecPerKm\" <= \"PaceMaxSecPerKm\")");
                    table.ForeignKey(
                        name: "FK_SpontaneousRuns_PublicPlaces_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "PublicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RunParticipants",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunParticipants", x => new { x.RunId, x.UserId });
                    table.ForeignKey(
                        name: "FK_RunParticipants_SpontaneousRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "SpontaneousRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RunParticipants_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Interests_ToUserId",
                table: "Interests",
                column: "ToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RunParticipants_UserId",
                table: "RunParticipants",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SpontaneousRuns_CreatedByUserId",
                table: "SpontaneousRuns",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SpontaneousRuns_PlaceId",
                table: "SpontaneousRuns",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_SpontaneousRuns_Status_StartsAtUtc",
                table: "SpontaneousRuns",
                columns: new[] { "Status", "StartsAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Interests");

            migrationBuilder.DropTable(
                name: "RunParticipants");

            migrationBuilder.DropTable(
                name: "SpontaneousRuns");
        }
    }
}
