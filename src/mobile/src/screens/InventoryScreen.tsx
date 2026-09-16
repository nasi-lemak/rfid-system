import { useEffect, useMemo, useRef, useState } from 'react';
import { FlatList, Pressable, Text, View } from 'react-native';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import type { RootStackParamList } from '../../App';
import { currentPosition, reportGps } from '../geo';
import { Api, type ItemSummary } from '../api/client';
import { useInventory, useReader } from '../reader';
import { useSettings } from '../store/settings';
import { Badge, Button, C, rssiTone, s } from '../ui';

/** Free scan: shows every tag in range, resolves it against the server and can post the reads as a handheld sighting. */
export default function InventoryScreen() {
  const nav = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const { reader, status } = useReader();
  const { settings } = useSettings();
  const [scanning, setScanning] = useState(false);
  const { tags, clear } = useInventory(scanning);
  const [resolved, setResolved] = useState<Record<string, ItemSummary | null>>({});
  const pending = useRef(new Set<string>());
  const [posted, setPosted] = useState<string | null>(null);

  useEffect(() => reader.onTrigger((pressed) => setScanning(pressed)), [reader]);

  // Resolve unknown EPCs lazily against the server (batched by the effect cadence).
  useEffect(() => {
    const missing = [...tags.keys()].filter((e) => !(e in resolved) && !pending.current.has(e)).slice(0, 10);
    if (missing.length === 0) return;
    missing.forEach((e) => pending.current.add(e));
    Promise.all(missing.map(async (epc) => { try { const r = await Api.byEpc(epc); return [epc, r.item] as const; } catch { return [epc, null] as const; } }))
      .then((rows) => { setResolved((prev) => { const n = { ...prev }; rows.forEach(([e, i]) => { n[e] = i; }); return n; }); rows.forEach(([e]) => pending.current.delete(e)); });
  }, [tags, resolved]);

  const list = useMemo(() => [...tags.values()].sort((a, b) => (b.rssi ?? -99) - (a.rssi ?? -99)), [tags]);
  const known = list.filter((t) => resolved[t.epc]).length;
  const post = async () => {
    const reads = list.map((t) => ({ epc: t.epc, rssi: t.rssi, locationId: settings.currentLocationId ?? undefined, readAt: new Date(t.lastAt).toISOString() }));
    try {
      const r = await Api.ingestHandheld(reads, `inv-${Date.now()}`);
      let gps = '';
      if (settings.gpsEnabled) { const g = await reportGps(reads.map((x) => x.epc), await currentPosition()); if (g) gps = ` · GPS ${g.fixes} fix(es)${g.alerts ? `, ${g.alerts} geofence alert(s)` : ''}`; }
      setPosted(`Sent ${r.received} reads · ${r.resolved} resolved · ${r.alerts} alert(s)${gps}`);
    } catch (e) { setPosted((e as Error).message); }
  };
  return (
    <View style={s.screen}>
      <View style={[s.row, { justifyContent: 'space-between', marginBottom: 10 }]}>
        <View><Text style={s.h2}>{list.length} tags · {known} known</Text><Text style={s.muted}>{status} · {settings.currentLocationName ?? 'no location set'}</Text></View>
        <View style={s.row}><Button small title="Clear" onPress={() => { clear(); setResolved({}); }} /><Button small title={scanning ? '■ Stop' : '▶ Scan'} tone={scanning ? 'danger' : 'primary'} onPress={() => setScanning(!scanning)} /></View>
      </View>
      <FlatList data={list} keyExtractor={(t) => t.epc} renderItem={({ item: t }) => {
        const it = resolved[t.epc];
        return (
          <Pressable onPress={() => nav.navigate('Lookup', { epc: t.epc })} style={[s.panel, { paddingVertical: 10 }]}>
            <View style={[s.row, { justifyContent: 'space-between' }]}>
              <Text style={[s.text, { fontWeight: '600', flex: 1 }]} numberOfLines={1}>{it ? it.name : it === null ? 'Unknown tag' : '…'}</Text>
              <Badge tone={rssiTone(t.rssi)}>{t.rssi != null ? `${Math.round(t.rssi)} dBm` : ''}</Badge>
              <Text style={s.muted}>×{t.count}</Text>
            </View>
            <Text style={s.mono}>{t.epc}</Text>
            {it && <Text style={s.muted}>{it.itemType?.name} · {it.state ?? ''} · {it.currentLocation?.name ?? '—'}{it.custodianParty ? ` · ${it.custodianParty.name}` : ''}</Text>}
          </Pressable>
        );
      }} ListEmptyComponent={<Text style={[s.muted, { textAlign: 'center', marginTop: 40 }]}>Press Scan or squeeze the trigger</Text>} />
      <View style={[s.row, { marginTop: 8 }]}>
        <Button title="Post as sighting" onPress={post} disabled={list.length === 0} style={{ flex: 1 }} />
        <Button title="Use in operation →" tone="primary" onPress={() => nav.navigate('Operation', { epcs: list.map((t) => t.epc) })} disabled={list.length === 0} style={{ flex: 1 }} />
      </View>
      {posted && <Text style={[s.muted, { marginTop: 6 }]}>{posted}</Text>}
      <Text style={[s.muted, { marginTop: 6, color: C.muted }]}>Posting a sighting updates "last seen" and triggers zone/loss rules without moving items.</Text>
    </View>
  );
}
