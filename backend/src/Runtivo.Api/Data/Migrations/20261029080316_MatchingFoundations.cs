using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Runtivo.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MatchingFoundations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PublicPlaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    City = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicPlaces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserBlocks",
                columns: table => new
                {
                    BlockerId = table.Column<Guid>(type: "uuid", nullable: false),
                    BlockedId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBlocks", x => new { x.BlockerId, x.BlockedId });
                    table.CheckConstraint("CK_UserBlocks_NotSelf", "\"BlockerId\" <> \"BlockedId\"");
                    table.ForeignKey(
                        name: "FK_UserBlocks_Users_BlockedId",
                        column: x => x.BlockedId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserBlocks_Users_BlockerId",
                        column: x => x.BlockerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunIntents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    HomePlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RadiusKm = table.Column<int>(type: "integer", nullable: false),
                    PaceMinSecPerKm = table.Column<int>(type: "integer", nullable: false),
                    PaceMaxSecPerKm = table.Column<int>(type: "integer", nullable: false),
                    DistanceMinMeters = table.Column<int>(type: "integer", nullable: false),
                    DistanceMaxMeters = table.Column<int>(type: "integer", nullable: false),
                    RunType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AvailabilityMask = table.Column<int>(type: "integer", nullable: false),
                    AllowPairs = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AdultConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunIntents", x => x.Id);
                    table.CheckConstraint("CK_RunIntents_Availability", "\"AvailabilityMask\" > 0");
                    table.CheckConstraint("CK_RunIntents_Distance", "\"DistanceMinMeters\" >= 1000 AND \"DistanceMinMeters\" <= \"DistanceMaxMeters\" AND \"DistanceMaxMeters\" <= 60000");
                    table.CheckConstraint("CK_RunIntents_Pace", "\"PaceMinSecPerKm\" BETWEEN 120 AND 1200 AND \"PaceMaxSecPerKm\" BETWEEN 120 AND 1200 AND \"PaceMinSecPerKm\" <= \"PaceMaxSecPerKm\"");
                    table.CheckConstraint("CK_RunIntents_Radius", "\"RadiusKm\" BETWEEN 5 AND 30");
                    table.ForeignKey(
                        name: "FK_RunIntents_PublicPlaces_HomePlaceId",
                        column: x => x.HomePlaceId,
                        principalTable: "PublicPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RunIntents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "PublicPlaces",
                columns: new[] { "Id", "City", "IsActive", "Latitude", "Longitude", "Name" },
                values: new object[,]
                {
                    { new Guid("5f1c2a10-0000-4000-8000-000000000001"), "Prato", true, 43.878999999999998, 11.096299999999999, "Piazza del Comune" },
                    { new Guid("5f1c2a10-0000-4000-8000-000000000002"), "Prato", true, 43.881, 11.099, "Piazza Mercatale" },
                    { new Guid("5f1c2a10-0000-4000-8000-000000000003"), "Firenze", true, 43.779499999999999, 11.215999999999999, "Parco delle Cascine" },
                    { new Guid("5f1c2a10-0000-4000-8000-000000000004"), "Firenze", true, 43.762900000000002, 11.265000000000001, "Piazzale Michelangelo" },
                    { new Guid("5f1c2a10-0000-4000-8000-000000000005"), "Firenze", true, 43.768700000000003, 11.262600000000001, "Piazza Santa Croce" },
                    { new Guid("5f1c2a10-0000-4000-8000-000000000006"), "Firenze", true, 43.780299999999997, 11.2432, "Fortezza da Basso" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublicPlaces_City_Name",
                table: "PublicPlaces",
                columns: new[] { "City", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunIntents_HomePlaceId",
                table: "RunIntents",
                column: "HomePlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_RunIntents_Status_ExpiresAt",
                table: "RunIntents",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RunIntents_UserId",
                table: "RunIntents",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserBlocks_BlockedId",
                table: "UserBlocks",
                column: "BlockedId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunIntents");

            migrationBuilder.DropTable(
                name: "UserBlocks");

            migrationBuilder.DropTable(
                name: "PublicPlaces");
        }
    }
}
