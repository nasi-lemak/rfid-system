import { useEffect, useState } from 'react';
import { FlatList, Pressable, ScrollView, Text, View } from 'react-native';
import { Api, type LocationRow } from '../api/client';
import { useReader } from '../reader';
import type { ReaderKind } from '../reader/types';
import { SimulatedReader } from '../reader/SimulatedReader';
import { clearQueue, readQueue, sync } from '../store/queue';
import { useSettings } from '../store/settings';
import { Badge, Button, C, Chips, Field, s } from '../ui';

export default function SettingsScreen() {
  const { settings, update } = useSettings();
  const { reader, status, error, reconnect } = useReader();
  const [locations, setLocations] = useState<LocationRow[]>([]);
  const [printers, setPrinters] = useState<{ id: string; name: string }[]>([]);
  const [filter, setFilter] = useState('');
  const [queue, setQueue] = useState(0);
  const [msg, setMsg] = useState<string | null>(null);
  const [power, setPower] = useState(String(settings.power));
  useEffect(() => { Api.locations().then(setLocations).catch(() => {}); readQueue().then((q) => setQueue(q.length)); Api.devices().then((d) => setPrinters(d.filter((x) => x.kind === 'Printer').map((x) => ({ id: x.id, name: x.name })))).catch(() => {}); }, []);

  const seedSimulator = async () => {
    if (!(reader instanceof SimulatedReader)) return;
    const r = await Api.items({ pageSize: 200 });
    reader.setPool(r.items.flatMap((i) => i.tags.map((t) => t.epc)));
    setMsg(`Simulator now sees ${reader.poolSize} tags from the server`);
  };
  return (
    <ScrollView style={s.screen} keyboardShouldPersistTaps="handled">
      <View style={s.panel}>
        <Text style={s.h2}>Reader</Text>
        <Chips<ReaderKind> options={[{ value: 'simulated', label: 'Simulated' }, { value: 'zebra', label: 'Zebra RFD40/8500' }, { value: 'chainway', label: 'Chainway C72/C66' }, { value: 'ble', label: 'BLE sled' }]} value={settings.readerKind} onChange={(v) => update({ readerKind: v })} />
        <View style={[s.row, { marginTop: 6 }]}><Badge tone={status === 'error' ? 'crit' : status === 'disconnected' ? 'warn' : 'ok'}>{status}</Badge><Button small title="Reconnect" onPress={reconnect} />{reader instanceof SimulatedReader && <Button small title="Seed simulator from server" onPress={seedSimulator} />}</View>
        {error && <Text style={{ color: C.warn, marginTop: 6 }}>{error}</Text>}
        <View style={[s.row, { marginTop: 8 }]}><View style={{ flex: 1 }}><Field label="Power (dBm, 5–30)" value={power} onChangeText={setPower} keyboardType="numeric" /></View><Button small title="Apply" onPress={() => { const p = Number(power); update({ power: p }); reader.setPower(p).catch(() => {}); }} style={{ marginTop: 26 }} /></View>
      </View>
      <View style={s.panel}>
        <Text style={s.h2}>Current location</Text>
        <Text style={s.muted}>Used as the location for sightings, stocktake scans and commissioned items.</Text>
        <Text style={[s.text, { marginVertical: 6 }]}>📍 {settings.currentLocationName ?? 'not set'}</Text>
        <Field label="Find" value={filter} onChangeText={setFilter} />
        <FlatList scrollEnabled={false} data={locations.filter((l) => !filter || l.name.toLowerCase().includes(filter.toLowerCase())).slice(0, 25)} keyExtractor={(l) => l.id} renderItem={({ item: l }) => (
          <Pressable onPress={() => update({ currentLocationId: l.id, currentLocationName: l.name })} style={[s.row, { paddingVertical: 8, borderBottomWidth: 1, borderBottomColor: C.border }]}><Badge>{l.kind}</Badge><Text style={s.text}>{'  '.repeat(Math.max(0, l.path.split('/').length - 3))}{l.name}</Text></Pressable>
        )} />
      </View>
      <View style={s.panel}>
        <Text style={s.h2}>Label printer</Text>
        <Text style={s.muted}>Network printers are driven by the server; "Bluetooth" uses a paired mobile printer via the BtPrinter native module.</Text>
        <Chips options={[{ value: 'none', label: 'None' }, ...printers.map((p) => ({ value: p.id, label: p.name })), { value: 'bluetooth', label: 'Bluetooth (mobile)' }]} value={settings.printerDeviceId ?? 'none'} onChange={(v) => update({ printerDeviceId: v === 'none' ? null : v, printerDeviceName: v === 'bluetooth' ? 'Bluetooth printer' : printers.find((p) => p.id === v)?.name ?? null })} />
      </View>
      <View style={s.panel}>
        <Text style={s.h2}>Offline queue</Text>
        <Text style={s.text}>{queue} pending operation(s)</Text>
        <View style={[s.row, { marginTop: 8 }]}><Button small title="Sync now" onPress={() => sync().then((r) => { setMsg(`Sent ${r.sent}, ${r.remaining} remaining`); readQueue().then((q) => setQueue(q.length)); })} /><Button small tone="danger" title="Discard queue" onPress={() => clearQueue().then(() => setQueue(0))} /></View>
      </View>
      <View style={s.panel}>
        <Text style={s.h2}>Account</Text>
        <Text style={s.text}>{settings.userName} @ {settings.serverUrl}</Text>
        <Button title="Sign out" tone="danger" onPress={() => update({ token: null, userName: null })} style={{ marginTop: 10 }} />
      </View>
      {msg && <Text style={[s.muted, { marginBottom: 20 }]}>{msg}</Text>}
    </ScrollView>
  );
}
