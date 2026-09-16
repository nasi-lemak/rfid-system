import { useEffect, useState } from 'react';
import { Alert, FlatList, Pressable, ScrollView, Text, View } from 'react-native';
import { Api, clearCache, type LocationRow } from '../api/client';
import { useReader } from '../reader';
import type { ReaderKind } from '../reader/types';
import { SimulatedReader } from '../reader/SimulatedReader';
import { clearQueue, clearRejected, readQueue, readRejected, sync, type RejectedOperation } from '../store/queue';
import { useSettings } from '../store/settings';
import { Badge, Button, C, Chips, Field, s } from '../ui';
import { LANGS, useT } from '../i18n';

export default function SettingsScreen() {
  const { settings, update } = useSettings();
  const t = useT();
  const { reader, status, error, reconnect } = useReader();
  const [locations, setLocations] = useState<LocationRow[]>([]);
  const [printers, setPrinters] = useState<{ id: string; name: string }[]>([]);
  const [filter, setFilter] = useState('');
  const [queue, setQueue] = useState(0);
  const [rejected, setRejected] = useState<RejectedOperation[]>([]);
  const refreshQueue = () => { readQueue().then((q) => setQueue(q.length)); readRejected().then(setRejected); };
  const [msg, setMsg] = useState<string | null>(null);
  const [power, setPower] = useState(String(settings.power));
  useEffect(() => { Api.locations().then(setLocations).catch(() => {}); refreshQueue(); Api.devices().then((d) => setPrinters(d.filter((x) => x.kind === 'Printer').map((x) => ({ id: x.id, name: x.name })))).catch(() => {}); }, []);

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
        <View style={[s.row, { marginTop: 8 }]}>
          <Button small title="Sync now" onPress={() => sync(true).then((r) => { setMsg(r.unauthorized ? 'Sign in again to sync' : `Sent ${r.sent}, ${r.remaining} remaining${r.failed ? `, ${r.failed} rejected` : ''}`); refreshQueue(); })} />
          <Button small tone="danger" title="Discard queue" disabled={queue === 0} onPress={() => Alert.alert(`Discard ${queue} pending operation(s)?`, 'They were never received by the server and cannot be recovered.', [{ text: 'Keep', style: 'cancel' }, { text: 'Discard', style: 'destructive', onPress: () => { void clearQueue().then(refreshQueue); } }])} />
        </View>
        {rejected.length > 0 && (
          <View style={{ marginTop: 12 }}>
            <Text style={[s.text, { color: C.warn, fontWeight: '700' }]}>{rejected.length} operation(s) rejected on sync</Text>
            <Text style={s.muted}>The server refused these when the queue was sent, or delivery gave up. They were not recorded – redo the work if it still applies.</Text>
            {rejected.slice().reverse().slice(0, 20).map((r) => (
              <View key={r.clientId} style={{ paddingVertical: 6, borderBottomWidth: 1, borderBottomColor: C.border }}>
                <Text style={s.text}>{String(r.request.operation ?? r.request.type ?? 'Operation')} · {Array.isArray(r.request.lines) ? `${(r.request.lines as unknown[]).length} tag(s)` : ''} · {new Date(r.createdAt).toLocaleString()}</Text>
                <Text style={[s.muted, { color: C.warn }]}>{r.error}</Text>
              </View>
            ))}
            {rejected.length > 20 && <Text style={s.muted}>… and {rejected.length - 20} older</Text>}
            <Button small title="Clear rejected list" onPress={() => Alert.alert('Clear the rejected list?', 'The list is only a record on this device; clearing it does not change anything on the server.', [{ text: 'Keep', style: 'cancel' }, { text: 'Clear', style: 'destructive', onPress: () => { void clearRejected().then(refreshQueue); } }])} style={{ marginTop: 8, alignSelf: 'flex-start' }} />
          </View>
        )}
      </View>
      <View style={s.panel}>
        <Text style={s.h2}>Offline data</Text>
        <Text style={s.muted}>Locations, parties, item types and floor plans are cached on the device so lookups, operations and the map keep working without coverage.</Text>
        <View style={[s.row, { marginTop: 8 }]}><Button small tone="primary" title="Download for offline" onPress={() => Api.prefetchOffline().then((r) => setMsg(`Cached ${r.locations} locations, ${r.parties} parties, ${r.itemTypes} item types, ${r.floorPlans} floor plans`)).catch((e) => setMsg((e as Error).message))} /><Button small title="Clear cache" onPress={() => clearCache().then((n) => setMsg(`Cleared ${n} cached entries`))} /></View>
      </View>
      <View style={s.panel}>
        <Text style={s.h2}>{t('Language')}</Text>
        <Chips options={LANGS.map((l) => ({ value: l.code, label: l.label }))} value={settings.language ?? 'en'} onChange={(v) => update({ language: v })} />
      </View>
      <View style={s.panel}>
        <Text style={s.h2}>{t('GPS')}</Text>
        <Text style={s.muted}>{t('Attach GPS position to scans and operations')}</Text>
        <View style={[s.row, { marginTop: 8 }]}><Badge tone={settings.gpsEnabled ? 'ok' : 'default'}>{settings.gpsEnabled ? 'on' : 'off'}</Badge><Button small title={settings.gpsEnabled ? 'Disable' : 'Enable'} onPress={() => update({ gpsEnabled: !settings.gpsEnabled })} /></View>
      </View>
      <View style={s.panel}>
        <Text style={s.h2}>Account</Text>
        <Text style={s.text}>{settings.userName} @ {settings.serverUrl}</Text>
        <Button title={t('Sign out')} tone="danger" onPress={() => Alert.alert(t('Sign out'), queue > 0 ? `${queue} operation(s) have not been sent yet. They stay on this device and sync after the next sign-in.` : 'You will need your credentials or a device token to sign in again.', [{ text: 'Cancel', style: 'cancel' }, { text: t('Sign out'), style: 'destructive', onPress: () => { void update({ token: null, userName: null, sessionExpired: false }); } }])} style={{ marginTop: 10 }} />
      </View>
      {msg && <Text style={[s.muted, { marginBottom: 20 }]}>{msg}</Text>}
    </ScrollView>
  );
}
