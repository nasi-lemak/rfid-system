import * as Location from 'expo-location';
import { Api, cachedGet, type FenceRow } from '../api/client';

export interface Position { lat: number; lng: number; accuracyM?: number | null; speedKph?: number | null; headingDeg?: number | null; at: string }

/** Current device position (asks for foreground permission once). Returns null when GPS is unavailable or denied. */
export async function currentPosition(): Promise<Position | null> {
  try {
    const perm = await Location.getForegroundPermissionsAsync();
    const granted = perm.granted || (await Location.requestForegroundPermissionsAsync()).granted;
    if (!granted) return null;
    const p = await Location.getCurrentPositionAsync({ accuracy: Location.Accuracy.Balanced });
    return { lat: p.coords.latitude, lng: p.coords.longitude, accuracyM: p.coords.accuracy, speedKph: p.coords.speed != null && p.coords.speed >= 0 ? p.coords.speed * 3.6 : null, headingDeg: p.coords.heading ?? null, at: new Date(p.timestamp).toISOString() };
  } catch { return null; }
}

/** Posts one GPS fix per scanned EPC so the server can geofence the items the handheld saw (best effort, never throws). */
export async function reportGps(epcs: string[], pos: Position | null) {
  if (!pos || epcs.length === 0) return null;
  try { return await Api.gps({ fixes: epcs.slice(0, 500).map((epc) => ({ epc, lat: pos.lat, lng: pos.lng, accuracyM: pos.accuracyM ?? undefined, speedKph: pos.speedKph ?? undefined, at: pos.at })) }); } catch { return null; }
}

// ── Geometry (same rules as the server) ──
export function distanceM(lat1: number, lng1: number, lat2: number, lng2: number) {
  const R = 6371000; const rad = (d: number) => (d * Math.PI) / 180;
  const dLat = rad(lat2 - lat1); const dLng = rad(lng2 - lng1);
  const a = Math.sin(dLat / 2) ** 2 + Math.cos(rad(lat1)) * Math.cos(rad(lat2)) * Math.sin(dLng / 2) ** 2;
  return 2 * R * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
}
export function insideFence(f: FenceRow, lat: number, lng: number) {
  if (f.kind === 'Circle') return f.centerLat != null && f.centerLng != null && f.radiusM != null && distanceM(lat, lng, f.centerLat, f.centerLng) <= f.radiusM;
  const pts = f.points ?? []; if (pts.length < 3) return false;
  let inside = false;
  for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) {
    const yi = pts[i].lat, xi = pts[i].lng, yj = pts[j].lat, xj = pts[j].lng;
    if (yi > lat !== yj > lat && lng < ((xj - xi) * (lat - yi)) / (yj - yi) + xi) inside = !inside;
  }
  return inside;
}
/** Metres from the position to the fence boundary (negative when inside a circle). */
export function distanceToFence(f: FenceRow, lat: number, lng: number) {
  if (f.kind === 'Circle' && f.centerLat != null && f.centerLng != null) return distanceM(lat, lng, f.centerLat, f.centerLng) - (f.radiusM ?? 0);
  const pts = f.points ?? []; if (pts.length === 0) return Infinity;
  const d = Math.min(...pts.map((p) => distanceM(lat, lng, p.lat, p.lng)));
  return insideFence(f, lat, lng) ? -d : d;
}
export const fences = () => cachedGet<FenceRow[]>('/api/geo/fences');
