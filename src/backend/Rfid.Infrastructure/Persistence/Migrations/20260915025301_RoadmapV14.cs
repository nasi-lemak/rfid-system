using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rfid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoadmapV14 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalIssuer",
                schema: "rfid",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSubject",
                schema: "rfid",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLoginAt",
                schema: "rfid",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RestrictToSites",
                schema: "rfid",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PositionTrack",
                schema: "rfid",
                table: "items",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "position_fixes",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    X = table.Column<double>(type: "double precision", nullable: false),
                    Y = table.Column<double>(type: "double precision", nullable: false),
                    AccuracyM = table.Column<double>(type: "double precision", nullable: true),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_position_fixes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_site_access",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_site_access", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "worker_leases",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Owner = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcquiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_leases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_position_fixes_ItemId_At",
                schema: "rfid",
                table: "position_fixes",
                columns: new[] { "ItemId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_position_fixes_TenantId_LocationId_At",
                schema: "rfid",
                table: "position_fixes",
                columns: new[] { "TenantId", "LocationId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_user_site_access_UserId_SiteLocationId",
                schema: "rfid",
                table: "user_site_access",
                columns: new[] { "UserId", "SiteLocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_worker_leases_Name",
                schema: "rfid",
                table: "worker_leases",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "position_fixes",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "user_site_access",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "worker_leases",
                schema: "rfid");

            migrationBuilder.DropColumn(
                name: "ExternalIssuer",
                schema: "rfid",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ExternalSubject",
                schema: "rfid",
                table: "users");

            migrationBuilder.DropColumn(
                name: "LastLoginAt",
                schema: "rfid",
                table: "users");

            migrationBuilder.DropColumn(
                name: "RestrictToSites",
                schema: "rfid",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PositionTrack",
                schema: "rfid",
                table: "items");
        }
    }
}
