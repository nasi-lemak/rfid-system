using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rfid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "rfid");

            migrationBuilder.CreateTable(
                name: "alerts",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RaisedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcknowledgedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "devices",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    SerialNumber = table.Column<string>(type: "text", nullable: true),
                    Model = table.Column<string>(type: "text", nullable: true),
                    SiteLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    TokenHash = table.Column<string>(type: "text", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Config = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "item_events",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    FromLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromPartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToPartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromState = table.Column<string>(type: "text", nullable: true),
                    ToState = table.Column<string>(type: "text", nullable: true),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Data = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "item_types",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    IsContainer = table.Column<bool>(type: "boolean", nullable: false),
                    TracksExpiry = table.Column<bool>(type: "boolean", nullable: false),
                    TracksCycles = table.Column<bool>(type: "boolean", nullable: false),
                    MaxCycles = table.Column<int>(type: "integer", nullable: true),
                    RequiresInspection = table.Column<bool>(type: "boolean", nullable: false),
                    InspectionIntervalDays = table.Column<int>(type: "integer", nullable: true),
                    ReorderPoint = table.Column<decimal>(type: "numeric", nullable: true),
                    Unit = table.Column<string>(type: "text", nullable: true),
                    AttributeSchema = table.Column<string>(type: "jsonb", nullable: false),
                    Lifecycle = table.Column<string>(type: "jsonb", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    Vertical = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_types", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "locations",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: true),
                    Path = table.Column<string>(type: "text", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    IsMobile = table.Column<bool>(type: "boolean", nullable: false),
                    Attributes = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_locations_locations_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "rfid",
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "operations",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    FromLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContainerItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetState = table.Column<string>(type: "text", nullable: true),
                    Reference = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DueBackAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "parties",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: true),
                    ExternalRef = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Attributes = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parties", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rules",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Trigger = table.Column<string>(type: "text", nullable: false),
                    Conditions = table.Column<string>(type: "jsonb", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Params = table.Column<string>(type: "jsonb", nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    Vertical = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "solution_templates",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Vertical = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Definition = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_solution_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stocktakes",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ExpectedCount = table.Column<int>(type: "integer", nullable: false),
                    FoundCount = table.Column<int>(type: "integer", nullable: false),
                    MissingCount = table.Column<int>(type: "integer", nullable: false),
                    UnexpectedCount = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stocktakes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tag_reads",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Epc = table.Column<string>(type: "text", nullable: false),
                    Tid = table.Column<string>(type: "text", nullable: true),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    AntennaPort = table.Column<int>(type: "integer", nullable: true),
                    Rssi = table.Column<double>(type: "double precision", nullable: true),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false),
                    SessionId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tag_reads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "antennas",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    PowerDbm = table.Column<double>(type: "double precision", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_antennas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_antennas_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "rfid",
                        principalTable: "devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_antennas_locations_LocationId",
                        column: x => x.LocationId,
                        principalSchema: "rfid",
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "operation_lines",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Epc = table.Column<string>(type: "text", nullable: true),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Result = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_operation_lines_operations_OperationId",
                        column: x => x.OperationId,
                        principalSchema: "rfid",
                        principalTable: "operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "items",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Identifier = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CurrentLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustodianPartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParentItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: true),
                    LotNumber = table.Column<string>(type: "text", nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CycleCount = table.Column<int>(type: "integer", nullable: false),
                    LastInspectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextInspectionDue = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DueBackAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSeenLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastSeenDeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    PurchasedAt = table.Column<DateOnly>(type: "date", nullable: true),
                    Attributes = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_items_item_types_ItemTypeId",
                        column: x => x.ItemTypeId,
                        principalSchema: "rfid",
                        principalTable: "item_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_items_items_ParentItemId",
                        column: x => x.ParentItemId,
                        principalSchema: "rfid",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_items_locations_CurrentLocationId",
                        column: x => x.CurrentLocationId,
                        principalSchema: "rfid",
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_items_parties_CustodianPartyId",
                        column: x => x.CustodianPartyId,
                        principalSchema: "rfid",
                        principalTable: "parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "stocktake_lines",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StocktakeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Epc = table.Column<string>(type: "text", nullable: true),
                    Expected = table.Column<bool>(type: "boolean", nullable: false),
                    Result = table.Column<string>(type: "text", nullable: false),
                    FoundAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FoundLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stocktake_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stocktake_lines_stocktakes_StocktakeId",
                        column: x => x.StocktakeId,
                        principalSchema: "rfid",
                        principalTable: "stocktakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                schema: "rfid",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Epc = table.Column<string>(type: "text", nullable: false),
                    Tid = table.Column<string>(type: "text", nullable: true),
                    Technology = table.Column<string>(type: "text", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    EncodedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tags_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "rfid",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alerts_TenantId_Status_RaisedAt",
                schema: "rfid",
                table: "alerts",
                columns: new[] { "TenantId", "Status", "RaisedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_antennas_DeviceId_Port",
                schema: "rfid",
                table: "antennas",
                columns: new[] { "DeviceId", "Port" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_antennas_LocationId",
                schema: "rfid",
                table: "antennas",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_item_events_ItemId_OccurredAt",
                schema: "rfid",
                table: "item_events",
                columns: new[] { "ItemId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_item_events_TenantId_OccurredAt",
                schema: "rfid",
                table: "item_events",
                columns: new[] { "TenantId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_item_types_TenantId_Code",
                schema: "rfid",
                table: "item_types",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_items_CurrentLocationId",
                schema: "rfid",
                table: "items",
                column: "CurrentLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_items_CustodianPartyId",
                schema: "rfid",
                table: "items",
                column: "CustodianPartyId");

            migrationBuilder.CreateIndex(
                name: "IX_items_ExpiryDate",
                schema: "rfid",
                table: "items",
                column: "ExpiryDate");

            migrationBuilder.CreateIndex(
                name: "IX_items_ItemTypeId",
                schema: "rfid",
                table: "items",
                column: "ItemTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_items_NextInspectionDue",
                schema: "rfid",
                table: "items",
                column: "NextInspectionDue");

            migrationBuilder.CreateIndex(
                name: "IX_items_ParentItemId",
                schema: "rfid",
                table: "items",
                column: "ParentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_items_TenantId_CurrentLocationId",
                schema: "rfid",
                table: "items",
                columns: new[] { "TenantId", "CurrentLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_items_TenantId_Identifier",
                schema: "rfid",
                table: "items",
                columns: new[] { "TenantId", "Identifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_items_TenantId_ItemTypeId_State",
                schema: "rfid",
                table: "items",
                columns: new[] { "TenantId", "ItemTypeId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_locations_ParentId",
                schema: "rfid",
                table: "locations",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_locations_TenantId_Code",
                schema: "rfid",
                table: "locations",
                columns: new[] { "TenantId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_locations_TenantId_Path",
                schema: "rfid",
                table: "locations",
                columns: new[] { "TenantId", "Path" });

            migrationBuilder.CreateIndex(
                name: "IX_operation_lines_ItemId",
                schema: "rfid",
                table: "operation_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_operation_lines_OperationId",
                schema: "rfid",
                table: "operation_lines",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_operations_Reference",
                schema: "rfid",
                table: "operations",
                column: "Reference");

            migrationBuilder.CreateIndex(
                name: "IX_operations_TenantId_StartedAt",
                schema: "rfid",
                table: "operations",
                columns: new[] { "TenantId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_parties_TenantId_Code",
                schema: "rfid",
                table: "parties",
                columns: new[] { "TenantId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_solution_templates_Code",
                schema: "rfid",
                table: "solution_templates",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stocktake_lines_StocktakeId_ItemId",
                schema: "rfid",
                table: "stocktake_lines",
                columns: new[] { "StocktakeId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_tag_reads_ItemId",
                schema: "rfid",
                table: "tag_reads",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_tag_reads_ReadAt",
                schema: "rfid",
                table: "tag_reads",
                column: "ReadAt");

            migrationBuilder.CreateIndex(
                name: "IX_tag_reads_TenantId_Epc_ReadAt",
                schema: "rfid",
                table: "tag_reads",
                columns: new[] { "TenantId", "Epc", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_tags_ItemId",
                schema: "rfid",
                table: "tags",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_tags_TenantId_Epc",
                schema: "rfid",
                table: "tags",
                columns: new[] { "TenantId", "Epc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tags_Tid",
                schema: "rfid",
                table: "tags",
                column: "Tid");

            migrationBuilder.CreateIndex(
                name: "IX_tenants_Code",
                schema: "rfid",
                table: "tenants",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_TenantId_Email",
                schema: "rfid",
                table: "users",
                columns: new[] { "TenantId", "Email" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alerts",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "antennas",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "item_events",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "operation_lines",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "rules",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "solution_templates",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "stocktake_lines",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "tag_reads",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "tags",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "tenants",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "users",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "devices",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "operations",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "stocktakes",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "items",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "item_types",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "locations",
                schema: "rfid");

            migrationBuilder.DropTable(
                name: "parties",
                schema: "rfid");
        }
    }
}
