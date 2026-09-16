import { useCallback, useEffect, useState } from 'react';
import { Pressable, ScrollView, Text, View } from 'react-native';
import { useFocusEffect, useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import type { RootStackParamList } from '../../App';
import { useReader } from '../reader';
import { useSettings } from '../store/settings';
import { readQueue, sync } from '../store/queue';
import { Badge, Button, C, s } from '../ui';
import { useT } from '../i18n';

const tiles: { to: keyof RootStackParamList; title: string; sub: string; icon: string }[] = [
  { to: 'Inventory', title: 'Scan / Inventory', sub: 'Read everything in range, see what it is', icon: '📡' },
  { to: 'Lookup', title: 'Lookup', sub: 'Identify one tag, history, quick actions', icon: '🔍' },
  { to: 'Locate', title: 'Locate', sub: 'Geiger-counter search for a specific item', icon: '🎯' },
  { to: 'Stocktake', title: 'Stocktake', sub: 'Count a location: found / missing / unexpected', icon: '📋' },
  { to: 'Operation', title: 'Operations', sub: 'Receive · Transfer · Issue · Return · Dispatch · Stage…', icon: '🔁' },
  { to: 'Commission', title: 'Commission', sub: 'Bind / encode tags to new items', icon: '🏷️' },
  { to: 'FloorPlan', title: 'Floor plan', sub: 'Anchors and live positions · works offline from the last sync', icon: '🗺️' },
  { to: 'Geofences', title: 'Map & geofences', sub: 'Your position against geofences · restricted-zone warnings', icon: '📍' },
];

export default function HomeScreen() {
  const nav = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const tr = useT();
  const { status, error, reconnect } = useReader();
  const { settings } = useSettings();
  const [queued, setQueued] = useState(0);
  const [syncMsg, setSyncMsg] = useState<string | null>(null);
  const refresh = useCallback(() => { readQueue().then((q) => setQueued(q.length)); }, []);
  useFocusEffect(refresh);
  useEffect(() => { if (queued > 0) sync().then((r) => { if (r.sent) setSyncMsg(`Synced ${r.sent} queued operation(s)`); refresh(); }); }, [queued, refresh]);
  const tone = status === 'ready' || status === 'inventory' || status === 'locating' ? 'ok' : status === 'error' ? 'crit' : 'warn';
  return (
    <ScrollView style={s.screen}>
      <View style={[s.panel, s.row, { justifyContent: 'space-between' }]}>
        <View><Text style={s.text}>Reader: {settings.readerKind}</Text><Text style={s.muted}>Location: {settings.currentLocationName ?? 'not set'}</Text></View>
        <View style={{ alignItems: 'flex-end', gap: 4 }}><Badge tone={tone}>{status}</Badge>{status === 'error' && <Button small title="Retry" onPress={reconnect} />}</View>
      </View>
      {error && <Text style={{ color: C.warn, marginBottom: 8 }}>{error}</Text>}
      {queued > 0 && <View style={[s.panel, s.row, { justifyContent: 'space-between', borderColor: C.warn }]}><Text style={s.text}>{queued} operation(s) waiting to sync</Text><Button small title="Sync now" onPress={() => sync().then((r) => { setSyncMsg(`Sent ${r.sent}, ${r.remaining} remaining`); refresh(); })} /></View>}
      {syncMsg && <Text style={[s.muted, { marginBottom: 8 }]}>{syncMsg}</Text>}
      {tiles.map((t) => (
        <Pressable key={t.to} onPress={() => nav.navigate(t.to as never)} style={({ pressed }) => [s.panel, s.row, { opacity: pressed ? 0.7 : 1 }]}>
          <Text style={{ fontSize: 28 }}>{t.icon}</Text>
          <View style={{ flex: 1 }}><Text style={[s.text, { fontWeight: '700' }]}>{tr(t.title)}</Text><Text style={s.muted}>{tr(t.sub)}</Text></View>
        </Pressable>
      ))}
      <Button title={tr('Settings')} onPress={() => nav.navigate('Settings')} style={{ marginTop: 4 }} />
    </ScrollView>
  );
}
