import { useCallback, useEffect, useState } from 'react';
import { ScrollView, Text, View, useWindowDimensions } from 'react-native';
import Svg, { Circle, G, Line, Polygon, Text as SvgText } from 'react-native-svg';
import type { FenceRow } from '../api/client';
import { currentPosition, distanceM, distanceToFence, fences as loadFences, insideFence, type Position } from '../geo';
import { useT } from '../i18n';
import { Badge, Button, C, Loading, s } from '../ui';

/** The handheld's own position against the tenant's geofences, drawn on a local metre grid (no map tiles needed offline). */
export default function GeofenceScreen() {
  const t = useT(); const { width } = useWindowDimensions();
  const [pos, setPos] = useState<Position | null>(null); const [fences, setFences] = useState<FenceRow[] | null>(null);
  const [offline, setOffline] = useState(false); const [busy, setBusy] = useState(false); const [error, setError] = useState<string | null>(null);
  const [radiusM, setRadiusM] = useState(2000);
  const refresh = useCallback(async () => {
    setBusy(true); setError(null);
    try { const f = await loadFences(); setFences(f.data.filter((x) => x.enabled)); setOffline(f.fromCache); const p = await currentPosition(); setPos(p); if (!p) setError('GPS unavailable or permission denied'); }
    catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }, []);
  useEffect(() => { refresh(); const id = setInterval(refresh, 15000); return () => clearInterval(id); }, [refresh]);
  const inside = pos && fences ? fences.filter((f) => insideFence(f, pos.lat, pos.lng)) : [];
  const restricted = inside.filter((f) => f.severity === 'Critical');
  const size = width - 48; const sc = size / (radiusM * 2);
  const toXY = (lat: number, lng: number) => { if (!pos) return { x: 0, y: 0 }; const dx = distanceM(pos.lat, pos.lng, pos.lat, lng) * (lng < pos.lng ? -1 : 1); const dy = distanceM(pos.lat, pos.lng, lat, pos.lng) * (lat > pos.lat ? -1 : 1); return { x: size / 2 + dx * sc, y: size / 2 + dy * sc }; };
  const colorFor = (f: FenceRow) => f.color || (f.severity === 'Critical' ? C.crit : f.severity === 'Warning' ? C.warn : C.primary);
  return (
    <ScrollView contentContainerStyle={{ padding: 12, gap: 10 }}>
      {restricted.length > 0 && <View style={[s.panel, { borderColor: C.crit, backgroundColor: '#3a1a18' }]}><Text style={[s.h2, { color: C.crit }]}>⚠ {restricted.map((f) => f.name).join(', ')}</Text><Text style={s.text}>You are inside a restricted zone.</Text></View>}
      <View style={[s.panel, s.row, { justifyContent: 'space-between' }]}>
        <View><Text style={s.h2}>{inside.length > 0 ? `${t('Inside fence')}: ${inside.map((f) => f.name).join(', ')}` : t('Outside all fences')}</Text><Text style={s.muted}>{pos ? `${pos.lat.toFixed(5)}, ${pos.lng.toFixed(5)} ± ${pos.accuracyM?.toFixed(0) ?? '?'} m` : '—'}{offline ? ' · offline fences' : ''}</Text></View>
        <Button small title={busy ? t('Working…') : t('Refresh')} onPress={refresh} disabled={busy} />
      </View>
      {error && <View style={[s.panel, { borderColor: C.warn }]}><Text style={s.text}>{error}</Text></View>}
      {!fences && <Loading />}
      {pos && fences && <View style={s.panel}>
        <Svg width={size} height={size}>
          <Circle cx={size / 2} cy={size / 2} r={size / 2 - 1} fill={C.bg} stroke={C.border} />
          {[0.25, 0.5, 0.75].map((k) => <Circle key={k} cx={size / 2} cy={size / 2} r={(size / 2) * k} fill="none" stroke={C.border} strokeDasharray="4 4" />)}
          <Line x1={size / 2} y1={0} x2={size / 2} y2={size} stroke={C.border} strokeOpacity={0.5} /><Line x1={0} y1={size / 2} x2={size} y2={size / 2} stroke={C.border} strokeOpacity={0.5} />
          {fences.map((f) => f.kind === 'Circle' && f.centerLat != null && f.centerLng != null
            ? <G key={f.id}><Circle cx={toXY(f.centerLat, f.centerLng).x} cy={toXY(f.centerLat, f.centerLng).y} r={Math.max(3, (f.radiusM ?? 0) * sc)} fill={colorFor(f)} fillOpacity={0.18} stroke={colorFor(f)} strokeWidth={2} /><SvgText x={toXY(f.centerLat, f.centerLng).x} y={toXY(f.centerLat, f.centerLng).y - Math.max(3, (f.radiusM ?? 0) * sc) - 4} fontSize={11} fill={C.text} textAnchor="middle">{f.name}</SvgText></G>
            : f.points.length >= 3 ? <G key={f.id}><Polygon points={f.points.map((p) => { const q = toXY(p.lat, p.lng); return `${q.x},${q.y}`; }).join(' ')} fill={colorFor(f)} fillOpacity={0.18} stroke={colorFor(f)} strokeWidth={2} /><SvgText x={toXY(f.points[0].lat, f.points[0].lng).x} y={toXY(f.points[0].lat, f.points[0].lng).y - 4} fontSize={11} fill={C.text}>{f.name}</SvgText></G> : null)}
          <Circle cx={size / 2} cy={size / 2} r={Math.max(6, (pos.accuracyM ?? 0) * sc)} fill={C.ok} fillOpacity={0.2} /><Circle cx={size / 2} cy={size / 2} r={7} fill={C.ok} stroke="#fff" strokeWidth={2} />
          <SvgText x={size - 6} y={size / 2 - 6} fontSize={10} fill={C.muted} textAnchor="end">{radiusM >= 1000 ? `${radiusM / 1000} km` : `${radiusM} m`}</SvgText>
        </Svg>
        <View style={[s.row, { marginTop: 8 }]}>{[500, 2000, 10000, 50000].map((r) => <Button key={r} small title={r >= 1000 ? `${r / 1000} km` : `${r} m`} tone={r === radiusM ? 'primary' : 'default'} onPress={() => setRadiusM(r)} />)}</View>
      </View>}
      {pos && fences && <View style={s.panel}>
        {fences.map((f) => { const d = distanceToFence(f, pos.lat, pos.lng); return <View key={f.id} style={[s.row, { justifyContent: 'space-between', paddingVertical: 5, borderBottomWidth: 1, borderBottomColor: C.border }]}><View style={{ flex: 1 }}><Text style={s.text}>{f.name}</Text><Text style={s.muted}>{f.kind === 'Circle' ? `${f.radiusM} m radius` : `${f.points.length} corners`}{f.location ? ` · ${f.location}` : ''}</Text></View><Badge tone={d <= 0 ? (f.severity === 'Critical' ? 'crit' : 'ok') : 'default'}>{d <= 0 ? t('Inside fence') : d < 1000 ? `${d.toFixed(0)} m` : `${(d / 1000).toFixed(1)} km`}</Badge></View>; })}
        {fences.length === 0 && <Text style={s.muted}>No geofences defined on the server.</Text>}
      </View>}
    </ScrollView>
  );
}
