import { useEffect, useMemo, useState } from 'react';
import { FlatList, Pressable, ScrollView, Text, View } from 'react-native';
import { useNavigation, useRoute, type RouteProp } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import type { RootStackParamList } from '../../App';
import { Api, type LocationRow, type OperationDefinitionRow, type OperationResult, type PartyRow } from '../api/client';
import { useInventory, useReader } from '../reader';
import { runOrQueue } from '../store/queue';
import { useSettings } from '../store/settings';
import { currentPosition, reportGps } from '../geo';
import { Badge, Button, C, Chips, Field, s } from '../ui';

/** Built-in shapes, used until the server's definitions arrive (or from cache when offline). */
const FALLBACK: OperationDefinitionRow[] = ([
  ['Receive', ['Activate', 'Move'], { toLocation: true }], ['Transfer', ['Move', 'SetCustodian'], { toLocation: true }], ['Issue', ['SetCustodian', 'SetDueBack', 'Move'], { party: true }],
  ['Return', ['ClearCustodian', 'ClearDueBack', 'Move'], {}], ['Count', ['RecordSeen'], {}], ['Dispatch', ['Move', 'SetCustodian', 'SetDueBack'], { toLocation: true }],
  ['Inspect', ['RecordInspection', 'SetState'], {}], ['Maintain', ['Move', 'SetState'], {}], ['ProcessStage', ['SetState', 'Move'], {}], ['Pack', ['Pack'], { container: true }],
  ['Unpack', ['Unpack', 'Move'], {}], ['Adjust', ['AdjustQuantity'], { quantity: true }], ['Dispose', ['Dispose'], {}],
] as [string, string[], Partial<OperationDefinitionRow['requires']>][]).map(([code, effects, req]) => ({
  code, name: code === 'ProcessStage' ? 'Stage' : code, description: '', baseType: code, isBuiltIn: true, enabled: true, itemTypeCodes: [],
  effects: effects.map((kind) => ({ kind, params: {} })),
  requires: { toLocation: false, party: false, container: false, targetState: false, quantity: false, fromStates: [], ...req },
}));
const has = (d: OperationDefinitionRow, kind: string) => d.effects.some((e) => e.kind === kind);

/** Scan a set of tags, choose what happens to them, submit (queued when offline). */
export default function OperationScreen() {
  const route = useRoute<RouteProp<RootStackParamList, 'Operation'>>();
  const nav = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const { reader } = useReader();
  const { settings } = useSettings();
  const [defs, setDefs] = useState<OperationDefinitionRow[]>(FALLBACK);
  const [type, setType] = useState<string>(route.params?.type ?? 'Transfer');
  const def = defs.find((d) => d.code === type) ?? FALLBACK.find((d) => d.code === type) ?? FALLBACK[1];
  // What the form asks for follows the definition: requirements are mandatory, effects make a field available.
  const ui = { needsTo: def.requires.toLocation, showTo: def.requires.toLocation || has(def, 'Move'), needsParty: def.requires.party, showParty: def.requires.party || has(def, 'SetCustodian'), needsContainer: def.requires.container || has(def, 'Pack'), showState: def.requires.targetState || has(def, 'SetState'), needsState: def.requires.targetState, needsQty: def.requires.quantity, showQty: def.requires.quantity || has(def, 'AdjustQuantity') };
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

  useEffect(() => { Api.operationDefinitions().then((d) => { const usable = d.filter((x) => x.enabled && x.baseType !== 'Commission'); if (usable.length) setDefs(usable); }).catch(() => {}); Api.locations().then(setLocations).catch(() => {}); Api.parties().then(setParties).catch(() => {}); Api.items({ pageSize: 200 }).then((r) => setContainers(r.items.filter((i) => i.itemType?.isContainer).map((i) => ({ id: i.id, name: `${i.name} (${i.identifier})` })))).catch(() => {}); }, []);
  useEffect(() => reader.onTrigger((p) => setScanning(p)), [reader]);
  const epcs = useMemo(() => [...new Set([...extra, ...tags.keys()])], [extra, tags]);

  const submit = async () => {
    setBusy(true); setMsg(null); setResult(null);
    try {
      const req: Record<string, unknown> = { type: def.baseType, operation: def.code, toLocationId: to?.id, partyId: party?.id, containerItemId: container?.id, targetState: targetState || undefined, reference: reference || undefined, lines: epcs.map((epc) => ({ epc, quantity: qty ? Number(qty) : undefined })) };
      const r = await runOrQueue(req);
      if (r.online) { setResult(r.result); if (settings.gpsEnabled) reportGps(epcs, await currentPosition()); if (r.result.rejected === 0 && r.result.unknown === 0) { setMsg(`✓ ${def.name}: ${r.result.ok} item(s)`); clear(); setExtra([]); } }
      else setMsg(`Offline – queued (${r.queued} pending). It will sync automatically.`);
    } catch (e) { setMsg((e as Error).message); } finally { setBusy(false); }
  };
  const valid = epcs.length > 0 && (!ui.needsTo || to) && (!ui.needsParty || party) && (!ui.needsContainer || container) && (!ui.needsQty || qty) && (!ui.needsState || targetState);

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
      <Chips options={defs.map((d) => ({ value: d.code, label: d.isBuiltIn ? d.name : `${d.icon ? d.icon + ' ' : ''}${d.name}` }))} value={type} onChange={(t) => { setType(t); setResult(null); }} />
      {!def.isBuiltIn && <Text style={[s.muted, { marginBottom: 6 }]}>{def.description || def.effects.map((e) => e.kind).join(' → ')}{def.itemTypeCodes.length ? ` · ${def.itemTypeCodes.join(', ')}` : ''}</Text>}
      {ui.showTo && <Pressable onPress={() => setPicker('to')} style={[s.panel, s.row, { justifyContent: 'space-between' }]}><Text style={s.muted}>{def.baseType === 'Return' || def.baseType === 'Unpack' ? 'Return to' : 'Destination'}{ui.needsTo ? ' *' : ''}</Text><Text style={s.text}>{to?.name ?? 'choose…'}</Text></Pressable>}
      {ui.showParty && <Pressable onPress={() => setPicker('party')} style={[s.panel, s.row, { justifyContent: 'space-between' }]}><Text style={s.muted}>Party{ui.needsParty ? ' *' : ''}</Text><Text style={s.text}>{party?.name ?? 'choose…'}</Text></Pressable>}
      {ui.needsContainer && <Pressable onPress={() => setPicker('container')} style={[s.panel, s.row, { justifyContent: 'space-between' }]}><Text style={s.muted}>Container *</Text><Text style={s.text}>{container?.name ?? 'choose…'}</Text></Pressable>}
      <Button small title={`📷 Scan barcodes (${extra.length} added)`} onPress={() => nav.navigate('Barcode', { continuous: true, title: 'Scan item barcodes', onScan: (code) => setExtra((prev) => (prev.includes(code) ? prev : [...prev, code])) })} style={{ marginBottom: 8 }} />
      {ui.showState && <Field label={`Target state${ui.needsState ? ' *' : ''}`} value={targetState} onChangeText={setTargetState} placeholder="e.g. InWash, Sterilised, Passed" />}
      {ui.showQty && <Field label={`Quantity delta${ui.needsQty ? ' *' : ''}`} value={qty} onChangeText={setQty} keyboardType="numbers-and-punctuation" placeholder="-5 or 20" />}
      <Field label="Reference" value={reference} onChangeText={setReference} placeholder="PO / work order / case…" />
      <View style={[s.row, { justifyContent: 'space-between', marginTop: 14, marginBottom: 6 }]}>
        <Text style={s.h2}>{epcs.length} tag(s)</Text>
        <View style={s.row}><Button small title="Clear" onPress={() => { clear(); setExtra([]); setResult(null); }} /><Button small title={scanning ? '■ Stop' : '▶ Scan'} tone={scanning ? 'danger' : 'primary'} onPress={() => setScanning(!scanning)} /></View>
      </View>
      {epcs.slice(0, 60).map((e) => { const line = result?.lines.find((l) => l.epc === e); return <View key={e} style={[s.row, { justifyContent: 'space-between', paddingVertical: 4, borderBottomWidth: 1, borderBottomColor: C.border }]}><Text style={s.mono}>{e}</Text>{line && <Badge tone={line.result === 'Ok' ? 'ok' : line.result === 'Unknown' ? 'info' : 'crit'}>{line.result === 'Ok' ? line.newState ?? 'ok' : line.message ?? line.result}</Badge>}</View>; })}
      {epcs.length > 60 && <Text style={s.muted}>… and {epcs.length - 60} more</Text>}
      {msg && <Text style={{ color: msg.startsWith('✓') ? C.ok : C.warn, marginVertical: 10 }}>{msg}</Text>}
      {result && (result.rejected > 0 || result.unknown > 0) && <Text style={{ color: C.warn, marginBottom: 8 }}>{result.ok} ok · {result.unknown} unknown · {result.rejected} rejected</Text>}
      <Button title={busy ? 'Submitting…' : `Submit ${def.name}`} tone="primary" onPress={submit} disabled={!valid || busy} style={{ marginVertical: 12 }} />
    </ScrollView>
  );
}
