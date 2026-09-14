import { useEffect, useMemo, useState } from 'react';
import { FlatList, Pressable, ScrollView, Text, View } from 'react-native';
import { useRoute, type RouteProp } from '@react-navigation/native';
import type { RootStackParamList } from '../../App';
import { Api, type LocationRow, type OperationResult, type PartyRow } from '../api/client';
import { useInventory, useReader } from '../reader';
import { runOrQueue } from '../store/queue';
import { Badge, Button, C, Chips, Field, s } from '../ui';

const TYPES = ['Receive', 'Transfer', 'Issue', 'Return', 'Count', 'Dispatch', 'Inspect', 'Maintain', 'ProcessStage', 'Pack', 'Unpack', 'Adjust', 'Dispose'] as const;
type OpType = typeof TYPES[number];
const needsTo: OpType[] = ['Receive', 'Transfer', 'Dispatch'];
const showTo: OpType[] = [...needsTo, 'Issue', 'Return', 'Maintain', 'Unpack', 'ProcessStage'];
const showParty: OpType[] = ['Issue', 'Transfer', 'Dispatch'];
const showState: OpType[] = ['ProcessStage', 'Inspect', 'Maintain'];

/** Scan a set of tags, choose what happens to them, submit (queued when offline). */
export default function OperationScreen() {
  const route = useRoute<RouteProp<RootStackParamList, 'Operation'>>();
  const { reader } = useReader();
  const [type, setType] = useState<OpType>((route.params?.type as OpType) ?? 'Transfer');
  const [scanning, setScanning] = useState(false);
  const { tags, clear } = useInventory(scanning);
  const [extra, setExtra] = useState<string[]>(route.params?.epcs ?? []);
  const [locations, setLocations] = useState<LocationRow[]>([]);
  const [parties, setParties] = useState<PartyRow[]>([]);
  const [containers, setContainers] = useState<{ id: string; name: string }[]>([]);
  const [to, setTo] = useState<LocationRow | null>(null);
  const [party, setParty] = useState<PartyRow | null>(null);
  const [container, setContainer] = useState<{ id: string; name: string } | null>(null);
  const [targetState, setTargetState] = useState('');
  const [reference, setReference] = useState('');
  const [qty, setQty] = useState('');
  const [filter, setFilter] = useState('');
  const [picker, setPicker] = useState<'to' | 'party' | 'container' | null>(null);
  const [result, setResult] = useState<OperationResult | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => { Api.locations().then(setLocations).catch(() => {}); Api.parties().then(setParties).catch(() => {}); Api.items({ pageSize: 200 }).then((r) => setContainers(r.items.filter((i) => i.itemType?.isContainer).map((i) => ({ id: i.id, name: `${i.name} (${i.identifier})` })))).catch(() => {}); }, []);
  useEffect(() => reader.onTrigger((p) => setScanning(p)), [reader]);
  const epcs = useMemo(() => [...new Set([...extra, ...tags.keys()])], [extra, tags]);

  const submit = async () => {
    setBusy(true); setMsg(null); setResult(null);
    try {
      const req: Record<string, unknown> = { type, toLocationId: to?.id, partyId: party?.id, containerItemId: container?.id, targetState: targetState || undefined, reference: reference || undefined, lines: epcs.map((epc) => ({ epc, quantity: qty ? Number(qty) : undefined })) };
      const r = await runOrQueue(req);
      if (r.online) { setResult(r.result); if (r.result.rejected === 0 && r.result.unknown === 0) { setMsg(`✓ ${type}: ${r.result.ok} item(s)`); clear(); setExtra([]); } }
      else setMsg(`Offline – queued (${r.queued} pending). It will sync automatically.`);
    } catch (e) { setMsg((e as Error).message); } finally { setBusy(false); }
  };
  const valid = epcs.length > 0 && (!needsTo.includes(type) || to) && (type !== 'Issue' || party) && (type !== 'Pack' || container) && (type !== 'Adjust' || qty);

  if (picker) {
    const rows = picker === 'to' ? locations.map((l) => ({ id: l.id, label: `${'  '.repeat(Math.max(0, l.path.split('/').length - 3))}${l.name}`, sub: l.kind, v: l })) : picker === 'party' ? parties.map((p) => ({ id: p.id, label: p.name, sub: p.kind, v: p })) : containers.map((c) => ({ id: c.id, label: c.name, sub: 'container', v: c }));
    const shown = rows.filter((r) => !filter || r.label.toLowerCase().includes(filter.toLowerCase()));
    return (
      <View style={s.screen}>
        <Field label={`Choose ${picker === 'to' ? 'destination' : picker}`} value={filter} onChangeText={setFilter} placeholder="filter…" autoFocus />
        <FlatList data={shown} keyExtractor={(r) => r.id} style={{ marginTop: 8 }} renderItem={({ item: r }) => (
          <Pressable onPress={() => { if (picker === 'to') setTo(r.v as LocationRow); else if (picker === 'party') setParty(r.v as PartyRow); else setContainer(r.v as { id: string; name: string }); setPicker(null); setFilter(''); }} style={[s.panel, s.row, { paddingVertical: 10, marginBottom: 6 }]}><Badge>{r.sub}</Badge><Text style={s.text}>{r.label}</Text></Pressable>
        )} />
        <Button title="Cancel" onPress={() => setPicker(null)} />
      </View>
    );
  }
  return (
    <ScrollView style={s.screen} keyboardShouldPersistTaps="handled">
      <Chips options={TYPES.map((t) => ({ value: t, label: t }))} value={type} onChange={(t) => { setType(t); setResult(null); }} />
      {showTo.includes(type) && <Pressable onPress={() => setPicker('to')} style={[s.panel, s.row, { justifyContent: 'space-between' }]}><Text style={s.muted}>{type === 'Return' || type === 'Unpack' ? 'Return to' : 'Destination'}{needsTo.includes(type) ? ' *' : ''}</Text><Text style={s.text}>{to?.name ?? 'choose…'}</Text></Pressable>}
      {showParty.includes(type) && <Pressable onPress={() => setPicker('party')} style={[s.panel, s.row, { justifyContent: 'space-between' }]}><Text style={s.muted}>Party{type === 'Issue' ? ' *' : ''}</Text><Text style={s.text}>{party?.name ?? 'choose…'}</Text></Pressable>}
      {type === 'Pack' && <Pressable onPress={() => setPicker('container')} style={[s.panel, s.row, { justifyContent: 'space-between' }]}><Text style={s.muted}>Container *</Text><Text style={s.text}>{container?.name ?? 'choose…'}</Text></Pressable>}
      {showState.includes(type) && <Field label="Target state" value={targetState} onChangeText={setTargetState} placeholder="e.g. InWash, Sterilised, Passed" />}
      {type === 'Adjust' && <Field label="Quantity delta *" value={qty} onChangeText={setQty} keyboardType="numbers-and-punctuation" placeholder="-5 or 20" />}
      <Field label="Reference" value={reference} onChangeText={setReference} placeholder="PO / work order / case…" />
      <View style={[s.row, { justifyContent: 'space-between', marginTop: 14, marginBottom: 6 }]}>
        <Text style={s.h2}>{epcs.length} tag(s)</Text>
        <View style={s.row}><Button small title="Clear" onPress={() => { clear(); setExtra([]); setResult(null); }} /><Button small title={scanning ? '■ Stop' : '▶ Scan'} tone={scanning ? 'danger' : 'primary'} onPress={() => setScanning(!scanning)} /></View>
      </View>
      {epcs.slice(0, 60).map((e) => { const line = result?.lines.find((l) => l.epc === e); return <View key={e} style={[s.row, { justifyContent: 'space-between', paddingVertical: 4, borderBottomWidth: 1, borderBottomColor: C.border }]}><Text style={s.mono}>{e}</Text>{line && <Badge tone={line.result === 'Ok' ? 'ok' : line.result === 'Unknown' ? 'info' : 'crit'}>{line.result === 'Ok' ? line.newState ?? 'ok' : line.message ?? line.result}</Badge>}</View>; })}
      {epcs.length > 60 && <Text style={s.muted}>… and {epcs.length - 60} more</Text>}
      {msg && <Text style={{ color: msg.startsWith('✓') ? C.ok : C.warn, marginVertical: 10 }}>{msg}</Text>}
      {result && (result.rejected > 0 || result.unknown > 0) && <Text style={{ color: C.warn, marginBottom: 8 }}>{result.ok} ok · {result.unknown} unknown · {result.rejected} rejected</Text>}
      <Button title={busy ? 'Submitting…' : `Submit ${type}`} tone="primary" onPress={submit} disabled={!valid || busy} style={{ marginVertical: 12 }} />
    </ScrollView>
  );
}
