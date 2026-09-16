import { useEffect, useState } from 'react';
import { ScrollView, Text, View } from 'react-native';
import { useNavigation, useRoute, type RouteProp } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import type { RootStackParamList } from '../../App';
import { Api, type ItemTypeRow } from '../api/client';
import { useReader } from '../reader';
import { useSettings } from '../store/settings';
import { runOrQueue } from '../store/queue';
import { Button, C, Chips, Field, s } from '../ui';
import { printLabel } from '../reader/Printer';

/**
 * Tag commissioning: read the tag in hand (or encode a new EPC into it), pick an item type,
 * enter identifier + required attributes, and bind. Runs the Commission operation on the server.
 */
export default function CommissionScreen() {
  const route = useRoute<RouteProp<RootStackParamList, 'Commission'>>();
  const nav = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const { reader } = useReader();
  const { settings } = useSettings();
  const [types, setTypes] = useState<ItemTypeRow[]>([]);
  const [pools, setPools] = useState<{ id: string; name: string; scheme: string; itemTypeId?: string | null }[]>([]);
  const [typeId, setTypeId] = useState<string | null>(null);
  const [epc, setEpc] = useState(route.params?.epc ?? '');
  const [newEpc, setNewEpc] = useState('');
  const [identifier, setIdentifier] = useState('');
  const [name, setName] = useState('');
  const [attrs, setAttrs] = useState<Record<string, string>>({});
  const [msg, setMsg] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [reading, setReading] = useState(false);
  const [lastItemId, setLastItemId] = useState<string | null>(null);
  useEffect(() => { Api.itemTypes().then(setTypes).catch((e) => setMsg((e as Error).message)); Api.pools().then(setPools).catch(() => {}); }, []);
  const allocate = async (poolId: string) => { setBusy(true); try { const r = await Api.allocateSerials(poolId, 1); setNewEpc(r.epcs[0]); setMsg(`Serial ${r.firstSerial} reserved from pool – write it to the tag`); } catch (e) { setMsg((e as Error).message); } finally { setBusy(false); } };
  const type = types.find((t) => t.id === typeId);

  useEffect(() => {
    if (!reading) return;
    let best: { epc: string; rssi: number } | null = null;
    const off = reader.onTag((e) => { const r = e.rssi ?? -99; if (!best || r > best.rssi) best = { epc: e.epc, rssi: r }; });
    reader.startInventory().catch(() => {});
    const t = setTimeout(() => { reader.stopInventory(); setReading(false); if (best) setEpc(best.epc); }, 1000);
    return () => { off(); clearTimeout(t); };
  }, [reading, reader]);

  const encode = async () => {
    setBusy(true); setMsg(null);
    try { await reader.writeEpc(newEpc.toUpperCase(), { currentEpc: epc || undefined }); setEpc(newEpc.toUpperCase()); setMsg('✓ EPC written to tag'); } catch (e) { setMsg((e as Error).message); } finally { setBusy(false); }
  };
  const commission = async () => {
    if (!type) return;
    setBusy(true); setMsg(null);
    try {
      const r = await runOrQueue({ type: 'Commission', toLocationId: settings.currentLocationId ?? undefined, lines: [{ epc, newItem: { itemTypeId: type.id, identifier: identifier || epc, name: name || identifier || epc, attributes: attrs } }] });
      if (r.online) { const l = r.result.lines[0]; setMsg(l.result === 'Ok' ? `✓ Commissioned ${l.itemName}` : `${l.result}: ${l.message}`); if (l.result === 'Ok') { setLastItemId(l.itemId ?? null); setEpc(''); setIdentifier(''); setName(''); setAttrs({}); } }
      else setMsg('Offline – queued for sync');
    } catch (e) { setMsg((e as Error).message); } finally { setBusy(false); }
  };
  return (
    <ScrollView style={s.screen} keyboardShouldPersistTaps="handled">
      <View style={s.panel}>
        <Text style={s.h2}>1. Tag</Text>
        <Field label="EPC or barcode in hand" value={epc} onChangeText={(v) => setEpc(v)} autoCapitalize="characters" />
        <View style={[s.row, { marginTop: 8 }]}><Button small title={reading ? 'Reading…' : '📡 Read nearest tag'} onPress={() => setReading(true)} disabled={reading} /><Button small title="📷 Barcode instead" onPress={() => nav.navigate('Barcode', { title: 'Scan the barcode to bind', onScan: (code) => { setEpc(code); if (!identifier) setIdentifier(code); } })} /></View>
        <Field label="Encode new EPC (optional, hex, 24 chars for 96-bit)" value={newEpc} onChangeText={(v) => setNewEpc(v.toUpperCase())} autoCapitalize="characters" placeholder="e.g. 3034F8B2000001000000ABCD" />
        {pools.length > 0 && <View style={{ marginTop: 8 }}><Text style={s.muted}>Next EPC from a GS1 serial pool:</Text><Chips options={pools.filter((p) => !typeId || !p.itemTypeId || p.itemTypeId === typeId).map((p) => ({ value: p.id, label: `${p.name} (${p.scheme})` }))} value={null} onChange={allocate} /></View>}
        <Button small title="Write EPC to tag" onPress={encode} disabled={busy || !/^[0-9A-F]{8,64}$/.test(newEpc) || newEpc.length % 4 !== 0} style={{ marginTop: 8 }} />
      </View>
      <View style={s.panel}>
        <Text style={s.h2}>2. Item</Text>
        <Chips options={types.map((t) => ({ value: t.id, label: t.name }))} value={typeId} onChange={(v) => { setTypeId(v); setAttrs({}); }} />
        <Field label="Identifier / serial" value={identifier} onChangeText={setIdentifier} placeholder="defaults to EPC" />
        <Field label="Name" value={name} onChangeText={setName} placeholder="defaults to identifier" />
        {type?.attributeSchema.map((a) => <Field key={a.name} label={`${a.name}${a.required ? ' *' : ''}`} value={attrs[a.name] ?? ''} onChangeText={(v) => setAttrs({ ...attrs, [a.name]: v })} />)}
        {type?.lifecycle?.initial && <Text style={[s.muted, { marginTop: 8 }]}>Initial state: {type.lifecycle.initial} · location: {settings.currentLocationName ?? 'none'}</Text>}
      </View>
      {msg && <Text style={{ color: msg.startsWith('✓') ? C.ok : C.warn, marginBottom: 10 }}>{msg}</Text>}
      {lastItemId && <Button title="🖨 Print label for last item" onPress={async () => { try { setMsg(await printLabel(lastItemId)); } catch (e) { setMsg((e as Error).message); } }} style={{ marginBottom: 8 }} />}
      <Button title={busy ? 'Working…' : 'Commission'} tone="primary" onPress={commission} disabled={busy || !type || epc.trim().length < 4 || (type?.attributeSchema.some((a) => a.required && !attrs[a.name]) ?? false)} />
    </ScrollView>
  );
}
