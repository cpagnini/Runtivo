using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Runtivo.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlaceSearchAndNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PublicPlaces_City_Name",
                table: "PublicPlaces");

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "RunIntents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCurated",
                table: "PublicPlaces",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "PublicPlaces",
                keyColumn: "Id",
                keyValue: new Guid("5f1c2a10-0000-4000-8000-000000000001"),
                column: "IsCurated",
                value: true);

            migrationBuilder.UpdateData(
                table: "PublicPlaces",
                keyColumn: "Id",
                keyValue: new Guid("5f1c2a10-0000-4000-8000-000000000002"),
                column: "IsCurated",
                value: true);

            migrationBuilder.UpdateData(
                table: "PublicPlaces",
                keyColumn: "Id",
                keyValue: new Guid("5f1c2a10-0000-4000-8000-000000000003"),
                column: "IsCurated",
                value: true);

            migrationBuilder.UpdateData(
                table: "PublicPlaces",
                keyColumn: "Id",
                keyValue: new Guid("5f1c2a10-0000-4000-8000-000000000004"),
                column: "IsCurated",
                value: true);

            migrationBuilder.UpdateData(
                table: "PublicPlaces",
                keyColumn: "Id",
                keyValue: new Guid("5f1c2a10-0000-4000-8000-000000000005"),
                column: "IsCurated",
                value: true);

            migrationBuilder.UpdateData(
                table: "PublicPlaces",
                keyColumn: "Id",
                keyValue: new Guid("5f1c2a10-0000-4000-8000-000000000006"),
                column: "IsCurated",
                value: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublicPlaces_City_Name_Latitude_Longitude",
                table: "PublicPlaces",
                columns: new[] { "City", "Name", "Latitude", "Longitude" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PublicPlaces_City_Name_Latitude_Longitude",
                table: "PublicPlaces");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "RunIntents");

            migrationBuilder.DropColumn(
                name: "IsCurated",
                table: "PublicPlaces");

            migrationBuilder.CreateIndex(
                name: "IX_PublicPlaces_City_Name",
                table: "PublicPlaces",
                columns: new[] { "City", "Name" },
                unique: true);
        }
    }
}
