using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rfid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoadmapV12 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "PositionAccuracyM",
                schema: "rfid",
                table: "items",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PositionAt",
                schema: "rfid",
                table: "items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PositionLocationId",
                schema: "rfid",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PositionX",
                schema: "rfid",
                table: "items",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PositionY",
                schema: "rfid",
                table: "items",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LabelDesign",
                schema: "rfid",
                table: "item_types",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApiToken",
                schema: "rfid",
                table: "integration_endpoints",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthType",
                schema: "rfid",
                table: "integration_endpoints",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ClientId",
                schema: "rfid",
                table: "integration_endpoints",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientSecret",
                schema: "rfid",
                table: "integration_endpoints",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Format",
                schema: "rfid",
                table: "integration_endpoints",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Mapping",
                schema: "rfid",
                table: "integration_endpoints",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Password",
                schema: "rfid",
                table: "integration_endpoints",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Scope",
                schema: "rfid",
                table: "integration_endpoints",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TokenUrl",
                schema: "rfid",
                table: "integration_endpoints",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                schema: "rfid",
                table: "integration_endpoints",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PathLossExponent",
                schema: "rfid",
                table: "antennas",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RssiAt1m",
                schema: "rfid",
                table: "antennas",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "X",
                schema: "rfid",
                table: "antennas",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Y",
                schema: "rfid",
                table: "antennas",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PositionAccuracyM",
                schema: "rfid",
                table: "items");

            migrationBuilder.DropColumn(
                name: "PositionAt",
                schema: "rfid",
                table: "items");

            migrationBuilder.DropColumn(
                name: "PositionLocationId",
                schema: "rfid",
                table: "items");

            migrationBuilder.DropColumn(
                name: "PositionX",
                schema: "rfid",
                table: "items");

            migrationBuilder.DropColumn(
                name: "PositionY",
                schema: "rfid",
                table: "items");

            migrationBuilder.DropColumn(
                name: "LabelDesign",
                schema: "rfid",
                table: "item_types");

            migrationBuilder.DropColumn(
                name: "ApiToken",
                schema: "rfid",
                table: "integration_endpoints");

            migrationBuilder.DropColumn(
                name: "AuthType",
                schema: "rfid",
                table: "integration_endpoints");

            migrationBuilder.DropColumn(
                name: "ClientId",
                schema: "rfid",
                table: "integration_endpoints");

            migrationBuilder.DropColumn(
                name: "ClientSecret",
                schema: "rfid",
                table: "integration_endpoints");

            migrationBuilder.DropColumn(
                name: "Format",
                schema: "rfid",
                table: "integration_endpoints");

            migrationBuilder.DropColumn(
                name: "Mapping",
                schema: "rfid",
                table: "integration_endpoints");

            migrationBuilder.DropColumn(
                name: "Password",
                schema: "rfid",
                table: "integration_endpoints");

            migrationBuilder.DropColumn(
                name: "Scope",
                schema: "rfid",
                table: "integration_endpoints");

            migrationBuilder.DropColumn(
                name: "TokenUrl",
                schema: "rfid",
                table: "integration_endpoints");

            migrationBuilder.DropColumn(
                name: "Username",
                schema: "rfid",
                table: "integration_endpoints");

            migrationBuilder.DropColumn(
                name: "PathLossExponent",
                schema: "rfid",
                table: "antennas");

            migrationBuilder.DropColumn(
                name: "RssiAt1m",
                schema: "rfid",
                table: "antennas");

            migrationBuilder.DropColumn(
                name: "X",
                schema: "rfid",
                table: "antennas");

            migrationBuilder.DropColumn(
                name: "Y",
                schema: "rfid",
                table: "antennas");
        }
    }
}
