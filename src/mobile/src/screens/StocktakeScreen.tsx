import { useEffect, useRef, useState } from 'react';
import { FlatList, Pressable, ScrollView, Text, View } from 'react-native';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import type { RootStackParamList } from '../../App';
import { Api, type LocationRow, type StocktakeSummary } from '../api/client';
import { useInventory, useReader } from '../reader';
import { useSettings } from '../store/settings';
import { Badge, Button, C, Field, Loading, s, Stat } from '../ui';

/**
 * Count a location. Scans are pushed to the server every couple of seconds so the web UI shows the
 * count live and so nothing is lost if the handheld dies mid-count.
 */
export default function StocktakeScreen() {
  const nav = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const { reader } = useReader();
  const { settings } = useSettings();
  const [open, setOpen] = useState<{ summary: StocktakeSummary; location?: string }[] | null>(null);
  const [locations, setLocations] = useState<LocationRow[]>([]);
  const [current, setCurrent] = useState<StocktakeSummary | null>(null);
  const [name, setName] = useState('');
  const [locFilter, setLocFilter] = useState('');
  const [scanning, setScanning] = useState(false);
  const { tags, clear } = useInventory(scanning);
  const sent = useRef(new Set<string>());
  const [msg, setMsg] = useState<string | null>(null);

  const load = () => { Api.stocktakes().then((r) => setOpen(r.filter((x) => x.summary.status === 'Open'))).catch((e) => setMsg((e as Error).message)); Api.locations().then(setLocations).catch(() => {}); };
  useEffect(load, []);
  useEffect(() => reader.onTrigger((p) => current && setScanning(p)), [reader, current]);

  // Push newly seen EPCs in batches.
  useEffect(() => {
    if (!current) return;
    const t = setInterval(async () => {
      const fresh = [...tags.keys()].filter((e) => !sent.current.has(e));
      if (fresh.length === 0) return;
      fresh.forEach((e) => sent.current.add(e));
      try { setCurrent(await Api.stocktakeScans(current.id, fresh, settings.currentLocationId ?? undefined)); } catch (e) { fresh.forEach((x) => sent.current.delete(x)); setMsg((e as Error).message); }
    }, 1500);
    return () => clearInterval(t);
  }, [current, tags, settings.currentLocationId]);

  const start = async (locationId: string, locName: string) => {
    try { const st = await Api.createStocktake(name || `${locName} ${new Date().toLocaleDateString()}`, locationId); setCurrent(st); sent.current.clear(); clear(); } catch (e) { setMsg((e as Error).message); }
  };
  const finish = async () => { if (!current) return; setScanning(false); try { const r = await Api.stocktakeReconcile(current.id); setMsg(`Reconciled: ${r.found} found, ${r.missing} missing, ${r.unexpected} unexpected. Apply the result from the web app.`); setCurrent(null); load(); } catch (e) { setMsg((e as Error).message); } };

  if (current) {
    const pct = current.expected ? Math.round((current.found / current.expected) * 100) : 0;
    return (
      <View style={s.screen}>
        <Text style={s.h2}>{current.name}</Text>
        <View style={[s.row, { marginBottom: 4 }]}><Stat label="Expected" value={current.expected} /><Stat label={`Found ${pct}%`} value={current.found} tone="ok" /><Stat label="Not seen" value={current.missing} tone="crit" /><Stat label="Unexpected" value={current.unexpected} tone="warn" /></View>
        <View style={{ height: 10, backgroundColor: C.bg, borderRadius: 5, borderWidth: 1, borderColor: C.border, overflow: 'hidden', marginBottom: 10 }}><View style={{ width: `${pct}%`, height: '100%', backgroundColor: C.ok }} /></View>
        <Text style={s.muted}>{tags.size} tags read on device · {current.unknown} unknown</Text>
        {msg && <Text style={{ color: C.warn, marginVertical: 6 }}>{msg}</Text>}
        <View style={{ flex: 1 }} />
        <View style={[s.row, { marginBottom: 8 }]}><Button title={scanning ? '■ Pause scanning' : '▶ Scan'} tone={scanning ? 'danger' : 'primary'} onPress={() => setScanning(!scanning)} style={{ flex: 1 }} /><Button title="📷 Barcode" onPress={() => nav.navigate('Barcode', { continuous: true, title: 'Scan item barcodes into the count', onScan: async (code) => { try { setCurrent(await Api.stocktakeScans(current.id, [code], settings.currentLocationId ?? undefined)); } catch (e) { setMsg((e as Error).message); } } })} /></View>
        <View style={s.row}><Button title="Leave open" onPress={() => { setScanning(false); setCurrent(null); load(); }} style={{ flex: 1 }} /><Button title="Finish & reconcile" tone="ok" onPress={finish} style={{ flex: 1 }} /></View>
      </View>
    );
  }
  const filtered = locations.filter((l) => !locFilter || l.name.toLowerCase().includes(locFilter.toLowerCase()));
  return (
    <ScrollView style={s.screen}>
      {msg && <Text style={{ color: C.warn, marginBottom: 8 }}>{msg}</Text>}
      <Text style={s.h2}>Continue an open stocktake</Text>
      {open === null ? <Loading /> : open.length === 0 ? <Text style={[s.muted, { marginBottom: 12 }]}>None open</Text> : open.map((o) => (
        <Pressable key={o.summary.id} onPress={() => { setCurrent(o.summary); sent.current.clear(); clear(); }} style={[s.panel, s.row, { justifyContent: 'space-between' }]}>
          <View><Text style={[s.text, { fontWeight: '600' }]}>{o.summary.name}</Text><Text style={s.muted}>{o.location}</Text></View>
          <Badge tone="ok">{o.summary.found}/{o.summary.expected}</Badge>
        </Pressable>
      ))}
      <Text style={[s.h2, { marginTop: 12 }]}>Start a new one</Text>
      <Field label="Name (optional)" value={name} onChangeText={setName} />
      <Field label="Find location" value={locFilter} onChangeText={setLocFilter} placeholder="type to filter" />
      <FlatList scrollEnabled={false} data={filtered.slice(0, 40)} keyExtractor={(l) => l.id} renderItem={({ item: l }) => (
        <Pressable onPress={() => start(l.id, l.name)} style={[s.panel, s.row, { paddingVertical: 10, marginBottom: 6 }]}><Badge>{l.kind}</Badge><Text style={s.text}>{'  '.repeat(Math.max(0, l.path.split('/').length - 3))}{l.name}</Text></Pressable>
      )} />
    </ScrollView>
  );
}
