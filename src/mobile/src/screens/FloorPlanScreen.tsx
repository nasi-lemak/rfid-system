import { useCallback, useEffect, useState } from 'react';
import { ScrollView, Text, View, useWindowDimensions } from 'react-native';
import { useRoute, type RouteProp } from '@react-navigation/native';
import Svg, { Circle, G, Line, Polyline, Rect, Text as SvgText } from 'react-native-svg';
import type { RootStackParamList } from '../../App';
import { Api, type FloorPlan, type FloorPlanSummary } from '../api/client';
import { Badge, Button, C, Chips, Loading, s } from '../ui';

/** Floor plan with anchors and last-known positions. Reads from the offline cache when the server is unreachable. */
export default function FloorPlanScreen() {
  const route = useRoute<RouteProp<RootStackParamList, 'FloorPlan'>>();
  const { width } = useWindowDimensions();
  const [plans, setPlans] = useState<FloorPlanSummary[] | null>(null);
  const [planId, setPlanId] = useState<string | undefined>(route.params?.locationId);
  const [plan, setPlan] = useState<FloorPlan | null>(null);
  const [offline, setOffline] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const highlight = route.params?.itemId;

  useEffect(() => { Api.floorPlans().then((r) => { setPlans(r.data); setOffline(r.fromCache ? r.cachedAt ?? 'cache' : null); if (!planId && r.data[0]) setPlanId(r.data[0].id); }).catch((e) => setError((e as Error).message)); }, []); // eslint-disable-line react-hooks/exhaustive-deps

  const load = useCallback(() => {
    if (!planId) return;
    setBusy(true); setError(null);
    Api.floorPlan(planId).then((r) => { setPlan(r.data); setOffline(r.fromCache ? r.cachedAt ?? 'cache' : null); }).catch((e) => setError((e as Error).message)).finally(() => setBusy(false));
  }, [planId]);
  useEffect(() => { load(); const t = setInterval(load, 5000); return () => clearInterval(t); }, [load]);

  const w = plan?.widthM ?? Math.max(10, ...(plan?.anchors.map((a) => a.x + 1) ?? [10]));
  const h = plan?.heightM ?? Math.max(10, ...(plan?.anchors.map((a) => a.y + 1) ?? [10]));
  const sc = Math.min((width - 48) / w, 420 / h);
  const items = plan?.items ?? [];
  const hl = items.find((i) => i.itemId === highlight);

  return (
    <ScrollView contentContainerStyle={{ padding: 12, gap: 10 }}>
      {plans && plans.length > 1 && <Chips options={plans.map((p) => ({ value: p.id, label: p.name }))} value={planId} onChange={setPlanId} />}
      {plans && plans.length === 0 && <View style={s.panel}><Text style={s.text}>No floor plans on this server: give antennas x/y coordinates and locations widthM/heightM.</Text></View>}
      {offline && <View style={[s.panel, { borderColor: C.warn }]}><Text style={s.text}>Offline · showing the plan cached {new Date(offline).toLocaleString()}</Text></View>}
      {error && <View style={[s.panel, { borderColor: C.crit }]}><Text style={[s.text, { color: C.crit }]}>{error}</Text></View>}
      {!plan && busy && <Loading />}
      {plan && (
        <View style={s.panel}>
          <View style={[s.row, { justifyContent: 'space-between', marginBottom: 6 }]}><Text style={s.h2}>{plan.location}</Text><Badge tone={offline ? 'warn' : 'ok'}>{items.length} positioned</Badge></View>
          <Svg width={w * sc + 24} height={h * sc + 24}>
            <G x={12} y={12}>
              <Rect x={0} y={0} width={w * sc} height={h * sc} fill={C.bg} stroke={C.border} strokeDasharray="6 4" />
              {Array.from({ length: Math.floor(w / 5) + 1 }).map((_, i) => <Line key={'gx' + i} x1={i * 5 * sc} y1={0} x2={i * 5 * sc} y2={h * sc} stroke={C.border} strokeOpacity={0.5} />)}
              {Array.from({ length: Math.floor(h / 5) + 1 }).map((_, i) => <Line key={'gy' + i} x1={0} y1={i * 5 * sc} x2={w * sc} y2={i * 5 * sc} stroke={C.border} strokeOpacity={0.5} />)}
              {plan.anchors.map((a) => <G key={a.antennaId} x={a.x * sc} y={a.y * sc}><Rect x={-6} y={-6} width={12} height={12} fill={C.warn} rx={2} /><SvgText x={9} y={4} fontSize={10} fill={C.muted}>{`#${a.port}`}</SvgText></G>)}
              {items.filter((i) => i.itemId !== highlight).map((i) => <G key={i.itemId} x={i.x * sc} y={i.y * sc}><Circle r={Math.max(5, (i.accuracyM ?? 1) * sc)} fill={C.primary} fillOpacity={0.15} /><Circle r={5} fill={C.primary} /><SvgText x={8} y={4} fontSize={10} fill={C.text}>{i.person ?? i.name}</SvgText></G>)}
              {hl && <G x={hl.x * sc} y={hl.y * sc}><Circle r={Math.max(8, (hl.accuracyM ?? 1) * sc)} fill={C.ok} fillOpacity={0.2} /><Polyline points={`${-12},0 ${12},0`} stroke={C.ok} strokeWidth={2} /><Polyline points={`0,${-12} 0,${12}`} stroke={C.ok} strokeWidth={2} /><Circle r={7} fill={C.ok} stroke="#fff" strokeWidth={2} /><SvgText x={10} y={-8} fontSize={12} fontWeight="bold" fill={C.ok}>{hl.person ?? hl.name}</SvgText></G>}
            </G>
          </Svg>
          <Text style={s.muted}>{w} m × {h} m · {plan.anchors.length} anchors{highlight && !hl ? ' · the selected item has no recent position here' : ''}</Text>
        </View>
      )}
      {items.length > 0 && <View style={s.panel}>
        {items.map((i) => <View key={i.itemId} style={[s.row, { justifyContent: 'space-between', paddingVertical: 4 }]}><Text style={[s.text, i.itemId === highlight && { color: C.ok, fontWeight: '600' }]}>{i.person ?? i.name}</Text><Text style={s.muted}>({i.x}, {i.y}) ± {i.accuracyM ?? '?'} m</Text></View>)}
      </View>}
      <Button title={busy ? 'Refreshing…' : 'Refresh'} onPress={load} disabled={busy} />
    </ScrollView>
  );
}
