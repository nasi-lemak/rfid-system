export type Guid = string;
export interface Paged<T> { items: T[]; total: number; page: number; pageSize: number }

export interface AttributeDefinition { name: string; type: string; required?: boolean; options?: string[] | null }
export interface LifecycleTransition { from: string; to: string; on: string; incrementCycle?: boolean }
export interface Lifecycle { initial?: string | null; states: string[]; transitions: LifecycleTransition[] }

export interface ItemType {
  id: Guid; name: string; code: string; category: 'Serialized' | 'Quantity'; isContainer: boolean; tracksExpiry: boolean; tracksCycles: boolean;
  maxCycles?: number | null; requiresInspection: boolean; inspectionIntervalDays?: number | null; reorderPoint?: number | null; unit?: string | null;
  attributeSchema: AttributeDefinition[]; lifecycle?: Lifecycle | null; vertical?: string | null; itemCount?: number; usefulLifeMonths?: number | null; hasLabelDesign?: boolean;
}

export interface Tag { id: Guid; epc: string; tid?: string | null; technology: string; status: string; itemId?: Guid | null; itemName?: string | null; encodedAt?: string | null }

export interface Item {
  id: Guid; itemTypeId: Guid; itemType?: Pick<ItemType, 'id' | 'name' | 'code' | 'category' | 'isContainer' | 'lifecycle' | 'attributeSchema' | 'maxCycles' | 'unit'> | null;
  identifier: string; name: string; state?: string | null; status: string; currentLocationId?: Guid | null;
  currentLocation?: { id: Guid; name: string; kind: string; code?: string } | null;
  custodianPartyId?: Guid | null; custodianParty?: { id: Guid; name: string; kind: string } | null;
  parentItemId?: Guid | null; parentItem?: { id: Guid; name: string; identifier: string } | null;
  quantity: number; unit?: string | null; lotNumber?: string | null; expiryDate?: string | null; cycleCount: number;
  lastInspectedAt?: string | null; nextInspectionDue?: string | null; dueBackAt?: string | null; lastSeenAt?: string | null; lastSeenLocationId?: Guid | null;
  cost?: number | null; purchasedAt?: string | null; attributes: Record<string, unknown>; createdAt: string; tags: Tag[];
}

export interface Location { id: Guid; parentId?: Guid | null; kind: string; name: string; code?: string | null; path: string; latitude?: number | null; longitude?: number | null; isMobile: boolean; attributes: Record<string, unknown>; itemCount?: number }
export interface Party { id: Guid; kind: string; name: string; code?: string | null; externalRef?: string | null; email?: string | null; attributes: Record<string, unknown>; itemsInCustody?: number }
export interface Antenna { id?: Guid; port: number; locationId?: Guid | null; locationName?: string | null; direction: 'None' | 'In' | 'Out'; powerDbm?: number | null; x?: number | null; y?: number | null; rssiAt1m?: number | null; pathLossExponent?: number | null }
export interface Device { id: Guid; name: string; kind: string; serialNumber?: string | null; model?: string | null; siteLocationId?: Guid | null; lastSeenAt?: string | null; config: Record<string, unknown>; hasToken: boolean; antennas: Antenna[]; llrp?: { host: string; port: number } | null; health?: string; healthChangedAt?: string | null; lastHeartbeatAt?: string | null; firmwareVersion?: string | null; heartbeatSlaMinutes?: number | null; trackedItemId?: Guid | null }

export interface ItemEvent { id: Guid; itemId: Guid; itemName?: string; itemIdentifier?: string; type: string; fromLocationId?: Guid | null; toLocationId?: Guid | null; fromPartyId?: Guid | null; toPartyId?: Guid | null; fromState?: string | null; toState?: string | null; operationId?: Guid | null; deviceId?: Guid | null; userId?: Guid | null; occurredAt: string; data: Record<string, unknown> }
export interface Alert { id: Guid; ruleId?: Guid | null; itemId?: Guid | null; itemName?: string; itemIdentifier?: string; locationId?: Guid | null; severity: 'Info' | 'Warning' | 'Critical'; message: string; status: 'Open' | 'Acknowledged' | 'Closed'; raisedAt: string; source?: string | null; deviceId?: Guid | null; escalationLevel?: number; nextEscalationAt?: string | null; acknowledgedAt?: string | null }
export interface RuleCondition { field: string; op: string; value: unknown }
export interface Rule { id: Guid; name: string; enabled: boolean; trigger: string; conditions: RuleCondition[]; action: string; params: Record<string, unknown>; severity: string; vertical?: string | null; notifyChannelIds?: Guid[]; escalationPolicyId?: Guid | null }

export interface OperationSummary { id: Guid; type: string; status: string; fromLocation?: string | null; toLocation?: string | null; party?: string | null; targetState?: string | null; reference?: string | null; notes?: string | null; user?: string | null; startedAt: string; completedAt?: string | null; lineCount: number; ok: number; rejected: number; unknown: number }
export interface OperationLineResult { epc?: string | null; itemId?: Guid | null; itemName?: string | null; result: string; message?: string | null; newState?: string | null }
export interface OperationResult { operationId: Guid; status: string; ok: number; unknown: number; rejected: number; lines: OperationLineResult[] }
export interface OperationRequest { type: string; fromLocationId?: Guid; toLocationId?: Guid; partyId?: Guid; containerItemId?: Guid; targetState?: string; reference?: string; notes?: string; dueBackAt?: string; lines: { epc?: string; itemId?: Guid; identifier?: string; quantity?: number; newItem?: { itemTypeId: Guid; identifier: string; name: string; attributes?: Record<string, unknown> } }[] }

export interface StocktakeSummary { id: Guid; name: string; status: string; expected: number; found: number; missing: number; unexpected: number; unknown: number }
export interface StocktakeListRow { summary: StocktakeSummary; locationId: Guid; location?: string; itemTypeId?: Guid | null; startedAt: string; completedAt?: string | null }
export interface StocktakeDetail extends StocktakeListRow { lines: { id: Guid; itemId?: Guid | null; epc?: string | null; expected: boolean; result: string; foundAt?: string | null; itemName?: string; itemIdentifier?: string; itemType?: string; expectedLocation?: string }[] }

export interface Template { id: Guid; code: string; name: string; vertical: string; description: string; installed: boolean; hasDemo: boolean; demoSeeded: boolean; demoSite?: string; itemTypes: { name: string; code: string; category: string; isContainer: boolean; tracksCycles: boolean; tracksExpiry: boolean; requiresInspection: boolean; states?: string[] | null; installed: boolean }[]; rules: { name: string; trigger: string; severity: string; action: string }[]; operations: string[]; locationKinds: string[] }

export interface SeedResult { scenario: string; skipped: boolean; locations: number; parties: number; items: number; devices: number; operations: number; rejectedLines: number; reads: number; alerts: number; warnings: string[] }
export interface Dashboard {
  totals: Record<string, number>;
  byStatus: { status: string; count: number }[]; byType: { type: string; count: number }[]; byState: { state: string; count: number }[]; byLocation: { location: string; count: number }[];
  readsPerDay: { day: string; count: number }[]; opsPerDay: { day: string; count: number }[];
}
export interface Lookups { operationTypes: string[]; locationKinds: string[]; partyKinds: string[]; deviceKinds: string[]; tagTechnologies: string[]; eventTypes: string[]; ruleActions: string[]; severities: string[]; itemStatuses: string[]; ruleFields: string[]; ruleOps: string[] }
export interface AuthUser { id: Guid; email: string; displayName: string; role: string; tenantId: Guid; tenantName?: string; restrictToSites?: boolean; sso?: string | null; sites?: { siteLocationId: Guid; siteName?: string | null; role: string }[] }
export interface HeatMap { locationId: Guid; cellM: number; widthM?: number | null; heightM?: number | null; from: string; to: string; cells: { ix: number; iy: number; x: number; y: number; samples: number; seconds: number; items: number }[] }
export interface PathHistory { item?: { id: Guid; name: string; identifier: string } | null; from: string; to: string; points: { x: number; y: number; accuracyM?: number | null; at: string; locationId: Guid }[] }
export interface HistoryItem { itemId: Guid; name?: string | null; identifier?: string | null; fixes: number; first: string; last: string }
export interface ClusterStatus { node: { id: string; startedAt: string; machine: string; pid: number; uptimeSeconds: number }; nodes: { id: string; leases: number; since: string; isThisNode: boolean }[]; leases: { name: string; owner: string; acquiredAt: string; expiresAt: string; active: boolean; heldByThisNode: boolean; version: number }[]; features: { redisBackplane: boolean; mqtt: boolean; llrp: boolean; sso: boolean; positionRetentionDays: number } }
export interface UserSites { id: Guid; restrictToSites: boolean; sites: { siteLocationId: Guid; siteName: string; sitePath: string; role: string }[] }

export interface PresentItem { itemId: Guid; name: string; identifier: string; itemType?: string | null; enteredAt: string; lastSeenAt: string; dwellMinutes: number; rssi?: number | null; person?: string | null }
export interface ZoneOccupancy { locationId: Guid; location: string; kind: string; present: number; items: PresentItem[] }
export interface MusterReport { onSite: number; accounted: number; unaccounted: number; accountedItems: PresentItem[]; unaccountedItems: PresentItem[] }
export interface TimingRow { itemId: Guid; bib: string; athlete?: string | null; category?: string | null; checkpoints: Record<string, string | null>; elapsedSeconds?: number | null; rank: number }
export interface ReportDef { code: string; name: string; description: string }
export interface ReportResult { columns: string[]; rows: unknown[][]; count: number }
export interface IntegrationEndpoint { id: Guid; name: string; url: string; hasSecret: boolean; format?: string; authType?: string; username?: string | null; tokenUrl?: string | null; clientId?: string | null; scope?: string | null; mapping?: Record<string, unknown>; hasCredentials?: boolean; enabled: boolean; eventTypes: string[]; includeAlerts: boolean; headers: Record<string, unknown>; batchSize: number; eventCursor: string; alertCursor: string; lastDeliveryAt?: string | null; lastError?: string | null; failureCount: number; nextAttemptAt?: string | null; deliveredCount: number }
export interface StocktakeSchedule { id: Guid; name: string; locationId: Guid; location?: string; itemTypeId?: Guid | null; intervalDays: number; timeOfDay: string; nextRunAt: string; lastRunAt?: string | null; lastStocktakeId?: Guid | null; enabled: boolean; autoReconcileHours?: number | null }

export interface FloorPlanSummary { id: Guid; name: string; kind: string; bounds?: { w: number; h: number } | null }
export interface FloorPlan { locationId: Guid; location: string; widthM?: number | null; heightM?: number | null; anchors: { antennaId: Guid; device: string; port: number; x: number; y: number }[]; items: { itemId: Guid; name: string; identifier: string; itemType?: string | null; x: number; y: number; accuracyM?: number | null; at?: string | null; person?: string | null }[] }
export interface LabelElement { type: 'text' | 'barcode' | 'qr' | 'box' | 'line'; x: number; y: number; width?: number | null; height?: number | null; text?: string | null; fontSizeMm: number; bold: boolean; rotation: number; moduleWidth: number; magnification: number; thickness: number }
export interface LabelDesign { widthMm: number; heightMm: number; dpmm: number; encodeRfid: boolean; elements: LabelElement[] }

export interface PrintJob { id: Guid; itemId: Guid; itemName?: string; itemIdentifier?: string; printerDeviceId: Guid; printer?: string; epc: string; status: string; reason: string; note?: string | null; copies: number; attempts: number; error?: string | null; requestedAt: string; requestedBy?: string | null; printedAt?: string | null; nextAttemptAt?: string | null }
export interface ImportRowResult { identifier: string; action: string; changes: string[]; error?: string | null }
export interface ImportResult { created: number; updated: number; unchanged: number; errors: number; dryRun: boolean; rows: ImportRowResult[] }
export interface ReconcileResult { matched: number; missingInPlatform: string[]; missingInErp: string[]; differences: { identifier: string; field: string; erp?: string | null; platform?: string | null }[] }
export interface LlrpStatus { device: string; endpoint?: { host: string; port: number } | null; options: { transmitPowerDbm?: number | null; session: number; tagPopulation: number; antennaIds?: number[] | null; gpiStartPort?: number | null; reportEveryNTags: number }; connected: boolean; connectedAt?: string | null; tagsReceived?: number; capabilities?: { manufacturer: string; modelId: number; firmware: string; maxAntennas: number; gpis: number; gpos: number; hasUtcClock: boolean; powerTable: { index: number; dbm: number }[] } | null }

// ── v1.5 ──
export interface DeviceHealthRow { deviceId: Guid; name: string; health: string; heartbeats: number; expected: number; uptimePercent: number; lastHeartbeatAt?: string | null; firmwareVersion?: string | null; slaMinutes: number }
export interface DeviceHealthReport { days: number; summary: Record<string, number>; rows: DeviceHealthRow[] }
export interface FirmwareRelease { id: Guid; vendor: string; model: string; version: string; url?: string | null; checksum?: string | null; notes?: string | null; releasedAt: string; isActive: boolean; devicesOnVersion: number; rollouts: Record<string, number> }
export interface FirmwareRollout { id: Guid; releaseId: Guid; version?: string; deviceId: Guid; device?: string; status: string; scheduledAt: string; startedAt?: string | null; completedAt?: string | null; error?: string | null; attempts: number; fromVersion?: string | null }
export interface GeoPoint { lat: number; lng: number }
export interface GeoFence { id: Guid; name: string; kind: 'Circle' | 'Polygon'; centerLat?: number | null; centerLng?: number | null; radiusM?: number | null; points: GeoPoint[]; locationId?: Guid | null; location?: string | null; trigger: 'Enter' | 'Exit' | 'Both'; severity: string; enabled: boolean; color?: string | null; maxDwellMinutes?: number | null; itemTypeId?: Guid | null; inside: number }
export interface MapItem { itemId: Guid; name: string; identifier: string; itemType?: string | null; lat: number; lng: number; at?: string | null; speedKph?: number | null; location?: string | null; fences: string[] }
export interface MapData { items: MapItem[]; fences: GeoFence[] }
export interface GpsTrack { item?: { id: Guid; name: string; identifier: string } | null; from: string; to: string; points: { lat: number; lng: number; at: string; speedKph?: number | null }[] }
export interface DashboardWidget { id: string; type: string; title: string; w: number; h: number; config: Record<string, unknown> }
export interface DashboardDef { id: Guid; name: string; isDefault: boolean; ownerUserId?: Guid | null; shared: boolean; widgets: DashboardWidget[]; editable: boolean }
export interface WidgetResult { id: string; type: string; data: unknown; error?: string | null }
export interface WidgetTypes { types: string[]; statFilters: string[]; breakdowns: string[]; trendMetrics: string[]; listSources: string[] }
export interface NotificationChannel { id: Guid; name: string; kind: 'Email' | 'Sms' | 'Teams' | 'Slack' | 'Webhook'; enabled: boolean; catchAll: boolean; minSeverity: string; config: Record<string, unknown>; createdAt?: string }
export interface ChannelRow { channel: NotificationChannel; sent7d: number; failed7d: number }
export interface EscalationStep { afterMinutes: number; channelIds: Guid[]; message?: string | null }
export interface EscalationPolicy { id: Guid; name: string; steps: EscalationStep[]; repeatLastStep: boolean; isDefault: boolean; minSeverity: string }
export interface NotificationLogRow { id: Guid; channelId: Guid; channel?: string; alertId?: Guid | null; recipient: string; subject: string; status: string; error?: string | null; sentAt: string; escalationLevel: number }
export interface TrendSeries { metric: string; bucketSize: string; buckets: { start: string; count: number }[]; total: number }
export interface UtilizationRow { itemTypeId: Guid; itemType: string; items: number; active: number; inCustody: number; movedInWindow: number; eventsInWindow: number; avgCycles: number; utilizationPercent: number }
export interface DwellRow { locationId: Guid; location: string; kind: string; sessions: number; distinctItems: number; avgMinutes: number; maxMinutes: number; p90Minutes: number }
export interface AccuracyReport { overallAccuracy?: number | null; stocktakes: number; rows: { id: Guid; name: string; location: string; at: string; expected: number; found: number; missing: number; unexpected: number; accuracy?: number | null }[] }
export interface AlertResponseRow { severity: string; raised: number; acknowledged: number; closed: number; openNow: number; avgMinutesToAck?: number | null; avgMinutesToClose?: number | null; escalated: number }
export interface WarehouseStatus { enabled: boolean; path: string; format: string; intervalMinutes: number; datasets: { name: string; snapshot: boolean; lastExportedTo?: string | null }[]; runs: WarehouseRun[] }
export interface WarehouseRun { id: Guid; dataset: string; from: string; to: string; rows: number; path: string; format: string; bytes: number; startedAt: string; completedAt?: string | null; error?: string | null; manual: boolean }

// ── v1.6 ──
export interface Anomaly { id: Guid; kind: string; status: string; score: number; observed: number; expected: number; windowStart: string; windowEnd: string; message: string; detectedAt: string; lastSeenAt: string; occurrences: number; alertId?: Guid | null; deviceId?: Guid | null; device?: string | null; itemId?: Guid | null; item?: string | null; locationId?: Guid | null; location?: string | null; details: Record<string, unknown> }
export interface HourProfile { hour: number; baseline: number; today: number }
export interface SerialPool { id: Guid; name: string; scheme: string; companyPrefix: string; reference: string; filter: number; nextSerial: number; maxSerial?: number | null; itemTypeId?: Guid | null; itemType?: string | null; notes?: string | null; remaining?: number | null; sampleEpc?: string | null }
export interface SchemeInfo { scheme: string; label: string; referenceLabel: string; referenceDigits: number; serialBits: number; filterDefault: number }
export interface EncodingRequest { name?: string; scheme: string; companyPrefix: string; reference: string; filter: number; poolId?: Guid | null; firstSerial?: number | null; count: number; mode: 'tags' | 'bind' | 'items'; itemTypeId?: Guid | null; itemIds?: Guid[] | null; identifierPrefix?: string | null; printerDeviceId?: Guid | null }
export interface EncodingPreview { count: number; firstSerial: number; lastSerial: number; sample: string[]; firstEpc?: string | null; lastEpc?: string | null; duplicates: number; warnings: string[]; targetItems: number }
export interface EncodingBatch { id: Guid; name: string; scheme: string; companyPrefix: string; reference: string; filter: number; firstSerial: number; lastSerial: number; count: number; mode: string; tagsCreated: number; itemsBound: number; printJobs: number; firstEpc?: string | null; lastEpc?: string | null; createdAt: string; user?: string | null }
export interface AuditEntry { id: Guid; userId?: Guid | null; userName?: string | null; deviceId?: Guid | null; method: string; path: string; action: string; entityType?: string | null; entityId?: Guid | null; statusCode: number; body?: string | null; ipAddress?: string | null; userAgent?: string | null; at: string; durationMs: number }
export interface AuditSummary { days: number; total: number; byUser: { userId?: Guid | null; userName?: string | null; count: number; failed: number; last: string }[]; byAction: { action: string; count: number }[]; actions: string[] }
export interface RetentionRow { id: Guid; dataset: string; description: string; protected: boolean; retainDays?: number | null; enabled: boolean; lastRunAt?: string | null; lastDeleted: number; totalDeleted: number; rows: number; expired: number }
