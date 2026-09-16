# Data Model

All tenant-scoped tables carry `TenantId` and `CreatedAt`. Isolation is enforced twice: an EF
global query filter in the application and a Postgres **row-level-security** policy
(`tenant_isolation`, over `current_setting('app.tenant_id')`) on every table that has a
`TenantId` — see `ARCHITECTURE.md` §8. JSONB columns are marked *(jsonb)*.

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
| `operation_definitions` | Id, Code (unique per tenant), Name, Description, BaseType (built-in family), EventType, Effects *(jsonb [{kind, params}])*, Requires *(jsonb {toLocation, party, container, targetState, quantity, fromStates})*, EventData *(jsonb)*, ItemTypeCodes *(jsonb)*, Enabled, IsBuiltIn, Vertical, Icon — tenant/template-defined operations; the 14 built-ins are code-defined in `OperationCatalog` and not stored |
| `operations` | Id, Type (base type: Receive/Transfer/Issue/Return/Count/Dispatch/Inspect/Maintain/Dispose/Pack/Unpack/ProcessStage/Commission/Adjust), **DefinitionCode** (the operation that ran, e.g. `Sterilise`), **ClientId** (idempotency key, unique per tenant), Status (Draft/Completed/Cancelled), FromLocationId, ToLocationId, PartyId, ContainerItemId, TargetState, Reference, Notes, DeviceId, UserId, StartedAt, CompletedAt |
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
| `solution_templates` | Id, Code, Name, Vertical, Description, Definition *(jsonb: itemTypes, rules, operationDefinitions, …)* — global, not tenant-scoped |

## Platform
| Table | Key columns |
|---|---|
| `outbox` | Id, Kind (`live.event` / `live.alert` / `webhook` / `notification`), Destination, Payload *(json)*, Attempts, NextAttemptAt, ProcessedAt, Error — written in the same commit as the state change, delivered by `OutboxDispatcher`; processed rows pruned by retention |
| `idempotency_keys` | Id, Scope (`read-batch`, …), Key, Response *(json)* — unique per tenant/scope/key; committed with the work it guards |
| `worker_leases`, `cluster_nodes` | Lease name, holder node, expiry, concurrency token — cluster coordination, global |

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
`on` is an **operation code**: a built-in type name (`Issue`, `ProcessStage`, …) or a template/
tenant-defined operation (`"on": "Sterilise"`). Running that operation moves items along the
matching transition; a custom operation whose code the lifecycle does not mention falls back to
its base type's transitions. If a type has no lifecycle, states are free-form and `SetState`
effects write `TargetState`. Every writer of `State` (operations, rules, manual edits) refuses a
state that is not in `states`.

## Indexes worth noting
- `tags(TenantId, Epc)` unique; `tags(Tid)`.
- `items(TenantId, CurrentLocationId)`, `items(TenantId, ItemTypeId, State)`,
  `items(ExpiryDate)`, `items(NextInspectionDue)`.
- `locations(Path)` text_pattern_ops for subtree (`LIKE '/1/4/%'`).
- `item_events(ItemId, OccurredAt DESC)`; `tag_reads(ReadAt)` (BRIN).
- `operations(TenantId, ClientId)` unique where not null; `operation_definitions(TenantId, Code)` unique.
- `outbox(ProcessedAt, NextAttemptAt, CreatedAt)`; `idempotency_keys(TenantId, Scope, Key)` unique.
