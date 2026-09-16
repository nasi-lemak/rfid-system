import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { del, get, post, put } from './client';
import type { Alert, Dashboard, SeedResult, FloorPlanSummary, FloorPlan, HeatMap, PathHistory, HistoryItem, ClusterStatus, UserSites, DeviceHealthReport, FirmwareRelease, FirmwareRollout, MapData, GpsTrack, DashboardDef, WidgetResult, WidgetTypes, DashboardWidget, ChannelRow, EscalationPolicy, NotificationLogRow, TrendSeries, UtilizationRow, DwellRow, AccuracyReport, AlertResponseRow, DeviceHealthRow, WarehouseStatus, Anomaly, HourProfile, SerialPool, SchemeInfo, EncodingBatch, AuditEntry, AuditSummary, RetentionRow, PrintJob, LlrpStatus, ZoneOccupancy, MusterReport, TimingRow, ReportDef, ReportResult, IntegrationEndpoint, StocktakeSchedule, Device, Item, ItemEvent, ItemType, Location, Lookups, OperationRequest, OperationResult, OperationSummary, Paged, Party, Rule, StocktakeDetail, StocktakeListRow, StocktakeSummary, Tag, Template } from './types';

export const useLookups = () => useQuery({ queryKey: ['lookups'], queryFn: () => get<Lookups>('/api/lookups'), staleTime: Infinity });
export const useDashboard = () => useQuery({ queryKey: ['dashboard'], queryFn: () => get<Dashboard>('/api/dashboard'), refetchInterval: 15000 });
export const useItemTypes = () => useQuery({ queryKey: ['item-types'], queryFn: () => get<ItemType[]>('/api/item-types') });
export const useLocations = () => useQuery({ queryKey: ['locations'], queryFn: () => get<Location[]>('/api/locations') });
export const useParties = (q?: string) => useQuery({ queryKey: ['parties', q], queryFn: () => get<Party[]>('/api/parties', { q }) });
export const useDevices = () => useQuery({ queryKey: ['devices'], queryFn: () => get<Device[]>('/api/devices') });
export const useRules = () => useQuery({ queryKey: ['rules'], queryFn: () => get<Rule[]>('/api/rules') });
export const useAlerts = (status: string | undefined) => useQuery({ queryKey: ['alerts', status], queryFn: () => get<Alert[]>('/api/alerts', { status: status ?? '' }), refetchInterval: 10000 });
export const useTemplates = () => useQuery({ queryKey: ['templates'], queryFn: () => get<Template[]>('/api/templates') });
export const useItems = (params: Record<string, unknown>) => useQuery({ queryKey: ['items', params], queryFn: () => get<Paged<Item>>('/api/items', params), placeholderData: (p) => p });
export const useItem = (id: string | undefined) => useQuery({ queryKey: ['item', id], queryFn: () => get<{ item: Item; children: { id: string; name: string; identifier: string; state?: string; itemType: string }[] }>(`/api/items/${id}`), enabled: !!id });
export const useItemEvents = (id: string | undefined) => useQuery({ queryKey: ['item-events', id], queryFn: () => get<ItemEvent[]>(`/api/items/${id}/events`), enabled: !!id });
export const useEvents = (params: Record<string, unknown>) => useQuery({ queryKey: ['events', params], queryFn: () => get<ItemEvent[]>('/api/events', params), refetchInterval: 10000 });
export const useOperations = (params: Record<string, unknown>) => useQuery({ queryKey: ['operations', params], queryFn: () => get<Paged<OperationSummary>>('/api/operations', params) });
export const useStocktakes = () => useQuery({ queryKey: ['stocktakes'], queryFn: () => get<StocktakeListRow[]>('/api/stocktakes') });
export const useStocktake = (id?: string) => useQuery({ queryKey: ['stocktake', id], queryFn: () => get<StocktakeDetail>(`/api/stocktakes/${id}`), enabled: !!id, refetchInterval: 5000 });
export const useTags = (params: Record<string, unknown>) => useQuery({ queryKey: ['tags', params], queryFn: () => get<Paged<Tag>>('/api/tags', params) });
export const useUnknownReads = () => useQuery({ queryKey: ['unknown-reads'], queryFn: () => get<{ epc: string; lastSeen: string; count: number }[]>('/api/tags/unknown-reads'), refetchInterval: 10000 });

export function useInvalidate() {
  const qc = useQueryClient();
  return (...keys: string[]) => keys.forEach((k) => qc.invalidateQueries({ queryKey: [k] }));
}

export function useRunOperation() {
  const inv = useInvalidate();
  return useMutation({ mutationFn: (req: OperationRequest) => post<OperationResult>('/api/operations', req), onSuccess: () => inv('items', 'item', 'item-events', 'events', 'operations', 'alerts', 'dashboard') });
}
export function useCreateStocktake() {
  const inv = useInvalidate();
  return useMutation({ mutationFn: (r: { name: string; locationId: string; itemTypeId?: string }) => post<StocktakeSummary>('/api/stocktakes', r), onSuccess: () => inv('stocktakes', 'dashboard') });
}
export function useStocktakeAction(id: string) {
  const inv = useInvalidate();
  return useMutation({ mutationFn: (action: 'reconcile' | 'apply' | 'cancel') => post<StocktakeSummary>(`/api/stocktakes/${id}/${action}`), onSuccess: () => inv('stocktakes', 'stocktake', 'items', 'alerts', 'dashboard') });
}
export function useStocktakeScan(id: string) {
  const inv = useInvalidate();
  return useMutation({ mutationFn: (epcs: string[]) => post<StocktakeSummary>(`/api/stocktakes/${id}/scans`, { epcs }), onSuccess: () => inv('stocktake', 'stocktakes') });
}
export function useAlertAction() {
  const inv = useInvalidate();
  return useMutation({ mutationFn: ({ id, action }: { id: string; action: 'acknowledge' | 'close' }) => post<Alert>(`/api/alerts/${id}/${action}`), onSuccess: () => inv('alerts', 'dashboard') });
}
export function useApplyTemplate() {
  const inv = useInvalidate();
  return useMutation({ mutationFn: (code: string) => post<{ itemTypesCreated: number; rulesCreated: number; skipped: number }>(`/api/templates/${code}/apply`), onSuccess: () => inv('templates', 'item-types', 'rules') });
}
export function useSeedDemo() {
  const inv = useInvalidate();
  return useMutation({ mutationFn: (code: string) => post<SeedResult>(`/api/templates/${code}/demo`), onSuccess: () => inv('templates', 'item-types', 'rules', 'items', 'locations', 'parties', 'devices', 'alerts', 'events', 'operations', 'dashboard', 'tags') });
}
export function useSave<T>(path: string, keys: string[]) {
  const inv = useInvalidate();
  return useMutation({ mutationFn: ({ id, body }: { id?: string; body: unknown }) => (id ? put<T>(`${path}/${id}`, body) : post<T>(path, body)), onSuccess: () => inv(...keys) });
}
export function useRemove(path: string, keys: string[]) {
  const inv = useInvalidate();
  return useMutation({ mutationFn: (id: string) => del(`${path}/${id}`), onSuccess: () => inv(...keys) });
}

export const useZones = (under?: string) => useQuery({ queryKey: ['presence-zones', under], queryFn: () => get<ZoneOccupancy[]>('/api/presence/zones', { under }), refetchInterval: 5000 });
export const useMuster = (siteId?: string) => useQuery({ queryKey: ['muster', siteId], queryFn: () => get<MusterReport>('/api/presence/muster', { siteId }), enabled: !!siteId, refetchInterval: 5000 });
export const useTiming = (eventLocationId?: string) => useQuery({ queryKey: ['timing', eventLocationId], queryFn: () => get<{ checkpoints: string[]; rows: TimingRow[] }>('/api/presence/timing', { eventLocationId }), enabled: !!eventLocationId, refetchInterval: 5000 });
export const useReportCatalog = () => useQuery({ queryKey: ['reports'], queryFn: () => get<ReportDef[]>('/api/reports'), staleTime: Infinity });
export const useReport = (code?: string, params?: Record<string, unknown>) => useQuery({ queryKey: ['report', code, params], queryFn: () => get<ReportResult>(`/api/reports/${code}`, params), enabled: !!code });
export const useIntegrations = () => useQuery({ queryKey: ['integrations'], queryFn: () => get<IntegrationEndpoint[]>('/api/integrations'), refetchInterval: 10000 });
export const useSchedules = () => useQuery({ queryKey: ['schedules'], queryFn: () => get<StocktakeSchedule[]>('/api/stocktake-schedules') });

export const useFloorPlans = () => useQuery({ queryKey: ['floor-plans'], queryFn: () => get<FloorPlanSummary[]>('/api/positions/floor-plans') });
export const useHeatMap = (locationId?: string, hours = 24, cellM = 1, enabled = true) => useQuery({ queryKey: ['heatmap', locationId, hours, cellM], queryFn: () => get<HeatMap>('/api/positions/heatmap', { locationId, from: new Date(Date.now() - hours * 3600e3).toISOString(), cellM }), enabled: !!locationId && enabled, refetchInterval: 15000 });
export const useHistoryItems = (locationId?: string, hours = 24) => useQuery({ queryKey: ['history-items', locationId, hours], queryFn: () => get<HistoryItem[]>('/api/positions/history/items', { locationId, from: new Date(Date.now() - hours * 3600e3).toISOString() }), enabled: !!locationId });
export const usePathHistory = (itemId?: string, hours = 24) => useQuery({ queryKey: ['path', itemId, hours], queryFn: () => get<PathHistory>('/api/positions/history', { itemId, from: new Date(Date.now() - hours * 3600e3).toISOString() }), enabled: !!itemId });
export const useCluster = () => useQuery({ queryKey: ['cluster'], queryFn: () => get<ClusterStatus>('/api/cluster'), refetchInterval: 5000 });
export const useUserSites = (userId?: string) => useQuery({ queryKey: ['user-sites', userId], queryFn: () => get<UserSites>(`/api/users/${userId}/sites`), enabled: !!userId });
export const useFloorPlan = (id?: string) => useQuery({ queryKey: ['floor-plan', id], queryFn: () => get<FloorPlan>(`/api/positions/floor-plans/${id}`), enabled: !!id, refetchInterval: 4000 });

export const usePrintJobs = (params: Record<string, unknown>) => useQuery({ queryKey: ['print-jobs', params], queryFn: () => get<PrintJob[]>('/api/labels/jobs', params), refetchInterval: 5000 });
export const useLlrpStatus = (deviceId?: string) => useQuery({ queryKey: ['llrp', deviceId], queryFn: () => get<LlrpStatus>(`/api/devices/${deviceId}/llrp/status`), enabled: !!deviceId, refetchInterval: 5000 });

// ── v1.5 ──
export const useDeviceHealth = (days = 7) => useQuery({ queryKey: ['device-health', days], queryFn: () => get<DeviceHealthReport>('/api/devices/health', { days }), refetchInterval: 15000 });
export const useFirmwareReleases = () => useQuery({ queryKey: ['firmware-releases'], queryFn: () => get<FirmwareRelease[]>('/api/firmware/releases') });
export const useFirmwareRollouts = () => useQuery({ queryKey: ['firmware-rollouts'], queryFn: () => get<FirmwareRollout[]>('/api/firmware/rollouts'), refetchInterval: 10000 });
export const useGeoMap = (hours = 168) => useQuery({ queryKey: ['geo-map', hours], queryFn: () => get<MapData>('/api/geo/map', { hours }), refetchInterval: 10000 });
export const useGpsTrack = (itemId?: string, hours = 24) => useQuery({ queryKey: ['gps-track', itemId, hours], queryFn: () => get<GpsTrack>('/api/geo/track', { itemId, from: new Date(Date.now() - hours * 3600e3).toISOString() }), enabled: !!itemId });
export const useDashboards = () => useQuery({ queryKey: ['dashboards'], queryFn: () => get<DashboardDef[]>('/api/dashboards') });
export const useWidgetTypes = () => useQuery({ queryKey: ['widget-types'], queryFn: () => get<WidgetTypes>('/api/dashboards/widget-types'), staleTime: Infinity });
export const useWidgetData = (widgets: DashboardWidget[] | undefined) => useQuery({ queryKey: ['widget-data', widgets], queryFn: () => post<WidgetResult[]>('/api/dashboards/evaluate', widgets), enabled: !!widgets && widgets.length > 0, refetchInterval: 15000 });
export const useChannels = () => useQuery({ queryKey: ['channels'], queryFn: () => get<ChannelRow[]>('/api/notifications/channels') });
export const usePolicies = () => useQuery({ queryKey: ['policies'], queryFn: () => get<EscalationPolicy[]>('/api/notifications/policies') });
export const useNotificationLog = (alertId?: string) => useQuery({ queryKey: ['notification-log', alertId], queryFn: () => get<NotificationLogRow[]>('/api/notifications/log', { alertId }), refetchInterval: 15000 });
export const useTrend = (metric: string, days: number, bucket: string) => useQuery({ queryKey: ['trend', metric, days, bucket], queryFn: () => get<TrendSeries>('/api/analytics/trend', { metric, days, bucket }) });
export const useUtilization = (days: number) => useQuery({ queryKey: ['utilization', days], queryFn: () => get<UtilizationRow[]>('/api/analytics/utilization', { days }) });
export const useDwell = (days: number) => useQuery({ queryKey: ['dwell', days], queryFn: () => get<DwellRow[]>('/api/analytics/dwell', { days }) });
export const useAccuracy = (days: number) => useQuery({ queryKey: ['accuracy', days], queryFn: () => get<AccuracyReport>('/api/analytics/accuracy', { days }) });
export const useAlertResponse = (days: number) => useQuery({ queryKey: ['alert-response', days], queryFn: () => get<AlertResponseRow[]>('/api/analytics/alert-response', { days }) });
export const useUptime = (days: number) => useQuery({ queryKey: ['uptime', days], queryFn: () => get<DeviceHealthRow[]>('/api/analytics/uptime', { days }) });
export const useWarehouse = () => useQuery({ queryKey: ['warehouse'], queryFn: () => get<WarehouseStatus>('/api/warehouse'), refetchInterval: 20000 });

// ── v1.6 ──
export const useAnomalies = (status: string) => useQuery({ queryKey: ['anomalies', status], queryFn: () => get<Anomaly[]>('/api/anomalies', { status }), refetchInterval: 30000 });
export const useReadProfile = (deviceId?: string, locationId?: string) => useQuery({ queryKey: ['read-profile', deviceId, locationId], queryFn: () => get<HourProfile[]>('/api/anomalies/profile', { deviceId, locationId }), enabled: !!(deviceId || locationId) });
export const usePools = () => useQuery({ queryKey: ['pools'], queryFn: () => get<SerialPool[]>('/api/encoding/pools') });
export const useSchemes = (companyPrefix: string) => useQuery({ queryKey: ['schemes', companyPrefix], queryFn: () => get<SchemeInfo[]>('/api/encoding/schemes', { companyPrefix }) });
export const useBatches = () => useQuery({ queryKey: ['batches'], queryFn: () => get<EncodingBatch[]>('/api/encoding/batches') });
export const useAudit = (params: Record<string, unknown>) => useQuery({ queryKey: ['audit', params], queryFn: () => get<Paged<AuditEntry>>('/api/audit', params) });
export const useAuditSummary = () => useQuery({ queryKey: ['audit-summary'], queryFn: () => get<AuditSummary>('/api/audit/summary') });
export const useRetention = () => useQuery({ queryKey: ['retention'], queryFn: () => get<RetentionRow[]>('/api/retention') });
