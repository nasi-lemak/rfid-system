import { useEffect, useState } from 'react';
import { Text, View } from 'react-native';
import { useRoute, type RouteProp } from '@react-navigation/native';
import type { RootStackParamList } from '../../App';
import { useReader } from '../reader';
import { Button, C, Field, s } from '../ui';

/** Geiger-counter search: the bar fills and the "beep rate" rises as the reader nears the tag. */
export default function LocateScreen() {
  const route = useRoute<RouteProp<RootStackParamList, 'Locate'>>();
  const { reader } = useReader();
  const [epc, setEpc] = useState(route.params?.epc ?? '');
  const [active, setActive] = useState(false);
  const [prox, setProx] = useState(0);
  const [rssi, setRssi] = useState<number | undefined>();
  const [peak, setPeak] = useState(0);

  useEffect(() => {
    if (!active || !epc) return;
    const off = reader.onLocate((p, r) => { setProx(p); setRssi(r); setPeak((x) => Math.max(x, p)); });
    reader.startLocate(epc).catch(() => {});
    return () => { off(); reader.stopLocate().catch(() => {}); };
  }, [active, epc, reader]);
  useEffect(() => reader.onTrigger((pressed) => setActive(pressed && !!epc)), [reader, epc]);

  const color = prox > 70 ? C.ok : prox > 35 ? C.warn : C.crit;
  return (
    <View style={s.screen}>
      {route.params?.name && <Text style={s.h2}>{route.params.name}</Text>}
      <Field label="Target EPC" value={epc} onChangeText={(v) => setEpc(v.toUpperCase())} autoCapitalize="characters" editable={!active} />
      <View style={[s.panel, { alignItems: 'center', marginTop: 16, paddingVertical: 30 }]}>
        <Text style={[s.bigValue, { color, fontSize: 64 }]}>{prox}%</Text>
        <Text style={s.muted}>{rssi != null ? `${rssi.toFixed(0)} dBm` : active ? 'searching…' : 'idle'} · peak {peak}%</Text>
        <View style={{ height: 26, width: '100%', backgroundColor: C.bg, borderRadius: 13, marginTop: 20, overflow: 'hidden', borderWidth: 1, borderColor: C.border }}>
          <View style={{ height: '100%', width: `${prox}%`, backgroundColor: color }} />
        </View>
        <View style={[s.row, { marginTop: 16 }]}>{Array.from({ length: 10 }).map((_, i) => <View key={i} style={{ width: 18, height: 18, borderRadius: 9, backgroundColor: prox >= (i + 1) * 10 ? color : C.border }} />)}</View>
      </View>
      <Button title={active ? '■ Stop' : '🎯 Start locating'} tone={active ? 'danger' : 'primary'} onPress={() => { setPeak(0); setActive(!active); }} disabled={!epc} />
      <Text style={[s.muted, { marginTop: 10 }]}>Sweep slowly; proximity uses RSSI so metal and liquids can mislead. Hold the trigger on a pistol-grip sled to search.</Text>
    </View>
  );
}
