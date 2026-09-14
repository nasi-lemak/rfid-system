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
export interface Device { id: Guid; name: string; kind: string; serialNumber?: string | null; model?: string | null; siteLocationId?: Guid | null; lastSeenAt?: string | null; config: Record<string, unknown>; hasToken: boolean; antennas: Antenna[]; llrp?: { host: string; port: number } | null }

export interface ItemEvent { id: Guid; itemId: Guid; itemName?: string; itemIdentifier?: string; type: string; fromLocationId?: Guid | null; toLocationId?: Guid | null; fromPartyId?: Guid | null; toPartyId?: Guid | null; fromState?: string | null; toState?: string | null; operationId?: Guid | null; deviceId?: Guid | null; userId?: Guid | null; occurredAt: string; data: Record<string, unknown> }
export interface Alert { id: Guid; ruleId?: Guid | null; itemId?: Guid | null; itemName?: string; itemIdentifier?: string; locationId?: Guid | null; severity: 'Info' | 'Warning' | 'Critical'; message: string; status: 'Open' | 'Acknowledged' | 'Closed'; raisedAt: string }
export interface RuleCondition { field: string; op: string; value: unknown }
export interface Rule { id: Guid; name: string; enabled: boolean; trigger: string; conditions: RuleCondition[]; action: string; params: Record<string, unknown>; severity: string; vertical?: string | null }

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
export interface AuthUser { id: Guid; email: string; displayName: string; role: string; tenantId: Guid; tenantName?: string }

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
