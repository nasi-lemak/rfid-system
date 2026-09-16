using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rfid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoadmapV17 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PortalPartyId",
                schema: "rfid",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Symbology",
                schema: "rfid",
                table: "tags",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "billing_cursors",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccruedTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_cursors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "epcis_captures",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Events = table.Column<int>(type: "integer", nullable: false),
                    Applied = table.Column<int>(type: "integer", nullable: false),
                    Rejected = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Errors = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_epcis_captures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Charges = table.Column<decimal>(type: "numeric", nullable: false),
                    Credits = table.Column<decimal>(type: "numeric", nullable: false),
                    Total = table.Column<decimal>(type: "numeric", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Lines = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    ItemEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    RateCardId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_forecasts",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Risk = table.Column<double>(type: "double precision", nullable: false),
                    Level = table.Column<string>(type: "text", nullable: false),
                    PredictedServiceAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PredictedBy = table.Column<string>(type: "text", nullable: true),
                    Factors = table.Column<string>(type: "jsonb", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlertId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_forecasts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rate_cards",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ItemTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartyKind = table.Column<string>(type: "text", nullable: true),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    DepositAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    CycleFee = table.Column<decimal>(type: "numeric", nullable: false),
                    DailyFee = table.Column<decimal>(type: "numeric", nullable: false),
                    FreeDays = table.Column<int>(type: "integer", nullable: false),
                    LateFeePerDay = table.Column<decimal>(type: "numeric", nullable: false),
                    LossFee = table.Column<decimal>(type: "numeric", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_cards", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_billing_cursors_TenantId",
                schema: "rfid",
                table: "billing_cursors",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_TenantId_Number",
                schema: "rfid",
                table: "invoices",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_InvoiceId",
                schema: "rfid",
                table: "ledger_entries",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_TenantId_PartyId_OccurredAt",
                schema: "rfid",
                table: "ledger_entries",
                columns: new[] { "TenantId", "PartyId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_forecasts_ItemId",
                schema: "rfid",
                table: "maintenance_forecasts",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_forecasts_TenantId_ComputedAt",
                schema: "rfid",
                table: "maintenance_forecasts",
                columns: new[] { "TenantId", "ComputedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_cursors",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "epcis_captures",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "invoices",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "ledger_entries",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "maintenance_forecasts",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "rate_cards",
                schema: "rfid");

            migrationBuilder.DropColumn(
                name: "PortalPartyId",
                schema: "rfid",
                table: "users");

            migrationBuilder.DropColumn(
                name: "Symbology",
                schema: "rfid",
                table: "tags");
        }
    }
}
