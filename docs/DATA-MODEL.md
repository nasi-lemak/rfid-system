# Data Model

All tables carry `TenantId` (scoped by a global query filter) and `CreatedAt`.
JSONB columns are marked *(jsonb)*.

## Identity & organisation
| Table | Key columns |
|---|---|
| `tenants` | Id, Name, Code |
| `users` | Id, Email, PasswordHash, DisplayName, Role (Admin/Operator/Viewer/Device), TenantId |
| `parties` | Id, Kind (Person/Employee/Customer/Supplier/Department/Vehicle/Other), Name, Code, ExternalRef, Attributes *(jsonb)* |

## Places
| Table | Key columns |
|---|---|
| `locations` | Id, ParentId, Kind (Site/Building/Floor/Room/Zone/Area/Rack/Shelf/Bin/Dock/Gate/Vehicle/Vessel/Cabinet/Locker/Yard/Customer/External), Name, Code, Path (materialised `/a/b/c/` for subtree queries), Latitude, Longitude, IsMobile, Attributes *(jsonb)* |

## Things
| Table | Key columns |
|---|---|
| `item_types` | Id, Name, Code, Category (Serialized/Quantity), IsContainer, TracksExpiry, TracksCycles, MaxCycles, RequiresInspection, InspectionIntervalDays, AttributeSchema *(jsonb)*, Lifecycle *(jsonb: states, initial, transitions)*, ImageUrl |
| `items` | Id, ItemTypeId, Identifier (serial/asset no.), Name, State, Status (Active/Missing/Disposed/Retired), CurrentLocationId, CustodianPartyId, ParentItemId (container), Quantity, Unit, LotNumber, ExpiryDate, CycleCount, LastInspectedAt, NextInspectionDue, LastSeenAt, LastSeenLocationId, LastSeenDeviceId, Attributes *(jsonb)*, Cost, PurchasedAt |
| `tags` | Id, Epc (unique per tenant), Tid, Technology (UhfGen2/HfNfc/Lf/Ble/Active/Barcode), ItemId (nullable until commissioned), Status (Active/Retired/Unassigned), EncodedAt |

## Devices
| Table | Key columns |
|---|---|
| `devices` | Id, Name, Kind (Handheld/Fixed/Portal/Gate/Cabinet/Shelf/Locker/Tunnel/Vehicle), SerialNumber, SiteLocationId, Token (hashed), LastSeenAt, Config *(jsonb)* |
| `antennas` | Id, DeviceId, Port, LocationId, Direction (None/In/Out), PowerDbm |

## Transactions & history
| Table | Key columns |
|---|---|
| `operations` | Id, Type (Receive/Transfer/Issue/Return/Count/Dispatch/Inspect/Maintain/Dispose/Pack/Unpack/ProcessStage/Commission/Adjust), Status (Draft/Completed/Cancelled), FromLocationId, ToLocationId, PartyId, ContainerItemId, TargetState, Reference, Notes, DeviceId, UserId, StartedAt, CompletedAt |
| `operation_lines` | Id, OperationId, Epc, ItemId, Quantity, Result (Ok/Unknown/Unexpected/Rejected), Message |
| `item_events` | Id, ItemId, Type (Created/Seen/Moved/CustodyChanged/StateChanged/Counted/QuantityChanged/Packed/Unpacked/Inspected/Maintained/Disposed/Commissioned/Alert), FromLocationId, ToLocationId, FromPartyId, ToPartyId, FromState, ToState, OperationId, DeviceId, UserId, OccurredAt, Data *(jsonb)* |
| `tag_reads` | Id, Epc, Tid, ItemId, DeviceId, AntennaPort, Rssi, ReadAt, LocationId, Source (Handheld/Fixed/Manual) — raw, high-volume, partition-friendly |
| `stocktakes` | Id, Name, LocationId, ItemTypeId, Status (Open/Reconciled/Applied/Cancelled), ExpectedCount, FoundCount, MissingCount, UnexpectedCount, StartedAt, CompletedAt, UserId |
| `stocktake_lines` | Id, StocktakeId, ItemId, Epc, Expected, Found, Result (Found/Missing/Unexpected/Unknown), FoundAt, FoundLocationId |

## Rules
| Table | Key columns |
|---|---|
| `rules` | Id, Name, Enabled, Trigger (event type), Conditions *(jsonb [{field, op, value}])*, Action (CreateAlert/SetState/Webhook), Params *(jsonb)*, Severity |
| `alerts` | Id, RuleId, ItemId, LocationId, Severity (Info/Warning/Critical), Message, Status (Open/Acknowledged/Closed), RaisedAt, AcknowledgedBy, ClosedAt |
| `solution_templates` | Id, Code, Name, Vertical, Description, Definition *(jsonb)* |

## Lifecycle definition (jsonb on `item_types`)
```json
{
  "initial": "Clean",
  "states": ["Clean", "Issued", "Soiled", "InWash", "Retired"],
  "transitions": [
    {"from": "Clean",  "to": "Issued", "on": "Issue"},
    {"from": "Issued", "to": "Soiled", "on": "Return"},
    {"from": "Soiled", "to": "InWash", "on": "ProcessStage", "incrementCycle": true},
    {"from": "InWash", "to": "Clean",  "on": "ProcessStage"},
    {"from": "*",      "to": "Retired","on": "Dispose"}
  ]
}
```
An operation of type `on` moves items along the matching transition. If a type
has no lifecycle, states are free-form and `ProcessStage` sets `TargetState`.

## Indexes worth noting
- `tags(TenantId, Epc)` unique; `tags(Tid)`.
- `items(TenantId, CurrentLocationId)`, `items(TenantId, ItemTypeId, State)`,
  `items(ExpiryDate)`, `items(NextInspectionDue)`.
- `locations(Path)` text_pattern_ops for subtree (`LIKE '/1/4/%'`).
- `item_events(ItemId, OccurredAt DESC)`; `tag_reads(ReadAt)` (BRIN).
