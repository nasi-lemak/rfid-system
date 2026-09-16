import { useEffect, useMemo, useState } from 'react';
import { FlatList, Pressable, ScrollView, Text, View } from 'react-native';
import { useNavigation, useRoute, type RouteProp } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import type { RootStackParamList } from '../../App';
import { Api, type LocationRow, type OperationDefinitionRow, type OperationResult, type PartyRow, type WorkflowRow, type WorkflowStepRow } from '../api/client';
import { useInventory, useReader } from '../reader';
import { newClientId, runOrQueue } from '../store/queue';
import { Badge, Button, C, Field, Loading, s } from '../ui';

/**
 * Runs a guided workflow step by step. Every step is an ordinary operation carrying the run id, workflow code and
 * step key, so it goes through the same offline queue as any other operation and the run's audit trail is the
 * operations themselves. Tags scanned at one step carry over to the next unless the step asks for a rescan.
 */
export default function WorkflowRunScreen() {
  const route = useRoute<RouteProp<RootStackParamList, 'WorkflowRun'>>();
  const nav = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const { reader } = useReader();
  const [wf, setWf] = useState<WorkflowRow | null>(null);
  const [defs, setDefs] = useState<OperationDefinitionRow[]>([]);
  const [locations, setLocations] = useState<LocationRow[]>([]);
  const [parties, setParties] = useState<PartyRow[]>([]);
  const [runId] = useState(() => `wf-${newClientId()}`);
  const [index, setIndex] = useState(0);
  const [scanning, setScanning] = useState(false);
  const { tags, clear } = useInventory(scanning);
  const [carried, setCarried] = useState<string[]>([]);                    // tags from the previous step
  const [stopped, setStopped] = useState<Set<string>>(new Set());          // tags rejected at a "stop" step
  const [to, setTo] = useState<LocationRow | null>(null);
  const [party, setParty] = useState<PartyRow | null>(null);
  const [targetState, setTargetState] = useState('');
  const [qty, setQty] = useState('');
  const [picker, setPicker] = useState<'to' | 'party' | null>(null);
  const [filter, setFilter] = useState('');
  const [result, setResult] = useState<OperationResult | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState<{ step: string; ok: number; rejected: number; queued: boolean }[]>([]);

  useEffect(() => {
    Api.workflows().then((all) => setWf(all.find((w) => w.code === route.params.code) ?? null)).catch(() => setWf(null));
    Api.operationDefinitions().then(setDefs).catch(() => {});
    Api.locations().then(setLocations).catch(() => {}); Api.parties().then(setParties).catch(() => {});
  }, [route.params.code]);
  useEffect(() => reader.onTrigger((p) => setScanning(p)), [reader]);

  const step: WorkflowStepRow | undefined = wf?.steps[index];
  const def = useMemo(() => defs.find((d) => d.code === step?.operation), [defs, step]);
  const scanned = useMemo(() => [...tags.keys()], [tags]);
  const epcs = useMemo(() => (step?.rescan || index === 0 ? scanned : [...new Set([...carried, ...scanned])]).filter((e) => !stopped.has(e)), [step, index, scanned, carried, stopped]);
  const has = (kind: string) => def?.effects.some((e) => e.kind === kind) ?? false;
  const asks = (k: string) => step?.ask.includes(k) ?? false;
  const needsTo = !!def?.requires.toLocation && !step?.fixed.toLocationId;
  const showTo = !step?.fixed.toLocationId && (needsTo || asks('toLocation') || (has('Move') && asks('toLocation')));
  const needsParty = !!def?.requires.party && !step?.fixed.partyId;
  const showParty = !step?.fixed.partyId && (needsParty || asks('party'));
  const needsState = !!def?.requires.targetState && !step?.fixed.targetState;
  const showState = !step?.fixed.targetState && (needsState || asks('targetState'));
  const needsQty = !!def?.requires.quantity; const showQty = needsQty || asks('quantity');
  const valid = !!step && epcs.length > 0 && (!needsTo || to) && (!needsParty || party) && (!needsState || targetState) && (!needsQty || qty);

  const submit = async () => {
    if (!wf || !step) return;
    setBusy(true); setMsg(null); setResult(null);
    try {
      const req: Record<string, unknown> = {
        operation: step.operation, type: def?.baseType, workflowRunId: runId, workflowCode: wf.code, workflowStep: step.key, clientId: `${runId}:${step.key}`,
        toLocationId: step.fixed.toLocationId ?? to?.id, partyId: step.fixed.partyId ?? party?.id, targetState: step.fixed.targetState ?? (targetState || undefined), containerItemId: step.fixed.containerItemId ?? undefined,
        reference: step.fixed.reference ?? `${wf.name} · ${runId}`, lines: epcs.map((epc) => ({ epc, quantity: qty ? Number(qty) : undefined })),
      };
      const r = await runOrQueue(req);
      if (r.online) {
        setResult(r.result);
        const rejected = r.result.lines.filter((l) => l.result !== 'Ok').map((l) => l.epc).filter((e): e is string => !!e);
        if (step.onRejected === 'stop' && rejected.length) setStopped((prev) => new Set([...prev, ...rejected]));
        setDone((d) => [...d, { step: step.title || step.key, ok: r.result.ok, rejected: r.result.rejected + r.result.unknown, queued: false }]);
        setMsg(`✓ ${step.title || step.key}: ${r.result.ok} ok${r.result.rejected ? `, ${r.result.rejected} rejected` : ''}${r.result.unknown ? `, ${r.result.unknown} unknown` : ''}`);
      } else { setDone((d) => [...d, { step: step.title || step.key, ok: epcs.length, rejected: 0, queued: true }]); setMsg(`Offline – step queued (${r.queued} pending); it will sync automatically.`); }
      setCarried(epcs);
    } catch (e) { setMsg((e as Error).message); } finally { setBusy(false); }
  };
  const next = () => { setIndex((i) => i + 1); setResult(null); setMsg(null); setTo(null); setParty(null); setTargetState(''); setQty(''); if (wf?.steps[index + 1]?.rescan) clear(); };
  const finished = !!wf && index >= wf.steps.length;

  if (!wf) return <View style={s.screen}>{wf === null ? <Text style={s.muted}>Workflow not found.</Text> : <Loading />}</View>;
  if (picker) {
    const rows = picker === 'to' ? locations.map((l) => ({ id: l.id, label: l.name, sub: l.kind, v: l })) : parties.map((p) => ({ id: p.id, label: p.name, sub: p.kind, v: p }));
    const shown = rows.filter((r) => !filter || r.label.toLowerCase().includes(filter.toLowerCase()));
    return <View style={s.screen}><Field label={`Choose ${picker === 'to' ? 'destination' : 'party'}`} value={filter} onChangeText={setFilter} placeholder="filter…" autoFocus />
      <FlatList data={shown} keyExtractor={(r) => r.id} style={{ marginTop: 8 }} renderItem={({ item: r }) => <Pressable onPress={() => { if (picker === 'to') setTo(r.v as LocationRow); else setParty(r.v as PartyRow); setPicker(null); setFilter(''); }} style={[s.panel, s.row, { paddingVertical: 10, marginBottom: 6 }]}><Badge>{r.sub}</Badge><Text style={s.text}>{r.label}</Text></Pressable>} />
      <Button title="Cancel" onPress={() => setPicker(null)} /></View>;
  }
  return (
    <ScrollView style={s.screen} keyboardShouldPersistTaps="handled">
      <View style={[s.panel, s.row, { justifyContent: 'space-between' }]}><Text style={s.h2}>{wf.icon ? `${wf.icon} ` : ''}{wf.name}</Text><Badge tone="info">{Math.min(index + 1, wf.steps.length)} / {wf.steps.length}</Badge></View>
      <View style={[s.row, { flexWrap: 'wrap', marginVertical: 6 }]}>{wf.steps.map((st, i) => <Badge key={st.key} tone={i < index ? 'ok' : i === index && !finished ? 'info' : 'default'}>{i + 1}. {st.title || st.key}</Badge>)}</View>
      {finished ? (
        <View style={s.panel}><Text style={s.h2}>Workflow complete</Text>
          {done.map((d, i) => <Text key={i} style={s.text}>• {d.step}: {d.ok} ok{d.rejected ? `, ${d.rejected} rejected` : ''}{d.queued ? ' (queued)' : ''}</Text>)}
          <Text style={[s.muted, { marginTop: 6 }]}>Run {runId}</Text>
          <Button title="Done" tone="primary" onPress={() => nav.goBack()} style={{ marginTop: 12 }} />
        </View>
      ) : step && (
        <>
          <View style={s.panel}><Text style={s.h2}>{step.title || step.key}</Text>{!!step.prompt && <Text style={s.text}>{step.prompt}</Text>}<Text style={s.muted}>Operation: {def?.name ?? step.operation}{step.optional ? ' · optional' : ''}{step.rescan ? ' · scan a fresh set of tags' : ''}</Text></View>
          {showTo && <Pressable onPress={() => setPicker('to')} style={[s.panel, s.row, { justifyContent: 'space-between' }]}><Text style={s.muted}>Destination{needsTo ? ' *' : ''}</Text><Text style={s.text}>{to?.name ?? 'choose…'}</Text></Pressable>}
          {showParty && <Pressable onPress={() => setPicker('party')} style={[s.panel, s.row, { justifyContent: 'space-between' }]}><Text style={s.muted}>Party{needsParty ? ' *' : ''}</Text><Text style={s.text}>{party?.name ?? 'choose…'}</Text></Pressable>}
          {showState && <Field label={`Target state${needsState ? ' *' : ''}`} value={targetState} onChangeText={setTargetState} placeholder="e.g. InWash, Passed" />}
          {showQty && <Field label={`Quantity${needsQty ? ' *' : ''}`} value={qty} onChangeText={setQty} keyboardType="numbers-and-punctuation" />}
          <View style={[s.row, { justifyContent: 'space-between', marginTop: 14, marginBottom: 6 }]}>
            <Text style={s.h2}>{epcs.length} tag(s){carried.length > 0 && !step.rescan ? ' (carried over)' : ''}</Text>
            <View style={s.row}><Button small title="Clear" onPress={() => { clear(); setCarried([]); setResult(null); }} /><Button small title={scanning ? '■ Stop' : '▶ Scan'} tone={scanning ? 'danger' : 'primary'} onPress={() => setScanning(!scanning)} /></View>
          </View>
          {epcs.slice(0, 60).map((e) => { const line = result?.lines.find((l) => l.epc === e); return <View key={e} style={[s.row, { justifyContent: 'space-between', paddingVertical: 4, borderBottomWidth: 1, borderBottomColor: C.border }]}><Text style={s.mono}>{e}</Text>{line && <Badge tone={line.result === 'Ok' ? 'ok' : line.result === 'Unknown' ? 'info' : 'crit'}>{line.result === 'Ok' ? line.newState ?? 'ok' : line.message ?? line.result}</Badge>}</View>; })}
          {stopped.size > 0 && <Text style={s.muted}>{stopped.size} tag(s) stopped at an earlier step are excluded.</Text>}
          {msg && <Text style={{ color: msg.startsWith('✓') || msg.startsWith('Offline') ? C.ok : C.warn, marginVertical: 10 }}>{msg}</Text>}
          <View style={[s.row, { marginVertical: 12 }]}>
            <Button title={busy ? 'Submitting…' : `Submit ${step.title || step.key}`} tone="primary" onPress={submit} disabled={!valid || busy || (!!result && result.ok > 0)} style={{ flex: 1 }} />
            {(result || step.optional) && <Button title={index + 1 < wf.steps.length ? (result ? 'Next step →' : 'Skip →') : 'Finish'} tone={result ? 'ok' : 'default'} onPress={next} style={{ flex: 1 }} />}
          </View>
        </>
      )}
    </ScrollView>
  );
}
