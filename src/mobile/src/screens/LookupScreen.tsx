import { useEffect, useState } from 'react';
import { ScrollView, Text, View } from 'react-native';
import { useNavigation, useRoute, type RouteProp } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import type { RootStackParamList } from '../../App';
import { Api, type ItemSummary } from '../api/client';
import { useReader } from '../reader';
import { Badge, Button, Field, Loading, s, C } from '../ui';
import { printLabel } from '../reader/Printer';

/** Identify a single tag: nearest tag wins while scanning. Shows the item, its history and quick actions. */
export default function LookupScreen() {
  const route = useRoute<RouteProp<RootStackParamList, 'Lookup'>>();
  const nav = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const { reader } = useReader();
  const [epc, setEpc] = useState(route.params?.epc ?? '');
  const [item, setItem] = useState<ItemSummary | null | undefined>(undefined);
  const [events, setEvents] = useState<{ id: string; type: string; occurredAt: string; toState?: string; data: Record<string, unknown> }[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [scanning, setScanning] = useState(false);
  const [printMsg, setPrintMsg] = useState<string | null>(null);
  const doPrint = async () => { try { setPrintMsg(await printLabel(item!.id)); } catch (e) { setPrintMsg((e as Error).message); } };

  useEffect(() => {
    if (!scanning) return;
    let best: { epc: string; rssi: number } | null = null;
    const off = reader.onTag((e) => { const r = e.rssi ?? -99; if (!best || r > best.rssi) best = { epc: e.epc, rssi: r }; });
    reader.startInventory().catch(() => {});
    const t = setTimeout(() => { reader.stopInventory(); setScanning(false); if (best) setEpc(best.epc); }, 1200);
    return () => { off(); clearTimeout(t); };
  }, [scanning, reader]);

  useEffect(() => {
    if (!epc || epc.length < 4) return;
    setBusy(true); setError(null);
    Api.byEpc(epc).then(async (r) => { setItem(r.item); if (r.item) setEvents(await Api.events(r.item.id)); else setEvents([]); })
      .catch((e) => { setItem(null); setEvents([]); if ((e as { status?: number }).status !== 404) setError((e as Error).message); })
      .finally(() => setBusy(false));
  }, [epc]);

  return (
    <ScrollView style={s.screen}>
      <Field label="EPC" value={epc} onChangeText={(v) => setEpc(v.toUpperCase())} autoCapitalize="characters" placeholder="Scan or type" />
      <View style={[s.row, { marginVertical: 10 }]}><Button title={scanning ? 'Reading…' : '📡 Read nearest tag'} tone="primary" onPress={() => setScanning(true)} disabled={scanning} style={{ flex: 1 }} /></View>
      {busy && <Loading />}
      {error && <Text style={{ color: C.crit }}>{error}</Text>}
      {item === null && epc.length >= 4 && !busy && <View style={s.panel}><Text style={s.h2}>Unknown tag</Text><Text style={s.muted}>This EPC is not commissioned.</Text><Button title="Commission it" tone="primary" onPress={() => nav.navigate('Commission', { epc })} style={{ marginTop: 10 }} /></View>}
      {item && (
        <>
          <View style={s.panel}>
            <Text style={s.h1}>{item.name}</Text>
            <Text style={s.muted}>{item.itemType?.name} · {item.identifier}</Text>
            <View style={[s.row, { marginVertical: 8 }]}>{item.state && <Badge tone="info">{item.state}</Badge>}<Badge tone={item.status === 'Active' ? 'ok' : item.status === 'Missing' ? 'warn' : 'crit'}>{item.status}</Badge></View>
            <Text style={s.text}>📍 {item.currentLocation?.name ?? 'Unknown location'}</Text>
            {item.custodianParty && <Text style={s.text}>👤 {item.custodianParty.name}{item.dueBackAt ? ` · due ${new Date(item.dueBackAt).toLocaleDateString()}` : ''}</Text>}
            {item.itemType?.category === 'Quantity' && <Text style={s.text}>📦 {item.quantity} {item.unit ?? ''}</Text>}
            {item.cycleCount > 0 && <Text style={s.text}>🔁 {item.cycleCount} cycles</Text>}
            {item.expiryDate && <Text style={s.text}>⏳ expires {item.expiryDate}</Text>}
            {Object.entries(item.attributes ?? {}).map(([k, v]) => <Text key={k} style={s.muted}>{k}: {String(v)}</Text>)}
          </View>
          <View style={[s.row, { marginBottom: 12 }]}>
            {['Transfer', 'Issue', 'Return', 'Count', 'ProcessStage', 'Inspect'].map((op) => <Button key={op} small title={op} onPress={() => nav.navigate('Operation', { epcs: [epc], type: op })} />)}
            <Button small title="Locate" tone="primary" onPress={() => nav.navigate('Locate', { epc, name: item.name })} />
            <Button small title="🖨 Print label" onPress={doPrint} />
          </View>
          {printMsg && <Text style={[s.muted, { marginBottom: 8 }]}>{printMsg}</Text>}
          <View style={s.panel}>
            <Text style={s.h2}>History</Text>
            {events.map((e) => <Text key={e.id} style={s.muted}>{new Date(e.occurredAt).toLocaleString()} · {e.type}{e.toState ? ` → ${e.toState}` : ''}{e.data?.direction ? ` (${String(e.data.direction)})` : ''}</Text>)}
          </View>
        </>
      )}
    </ScrollView>
  );
}
