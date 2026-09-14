import { ActivityIndicator, Pressable, StyleSheet, Text, TextInput, View, type TextInputProps, type ViewStyle } from 'react-native';
import type { ReactNode } from 'react';

export const C = { bg: '#0f151c', panel: '#171f29', border: '#26313d', text: '#e6ebf0', muted: '#8e9aa8', primary: '#2f7fe0', ok: '#2fb673', warn: '#e39b2b', crit: '#e3503f', info: '#4b8fe8' };

export const s = StyleSheet.create({
  screen: { flex: 1, backgroundColor: C.bg, padding: 16 },
  panel: { backgroundColor: C.panel, borderRadius: 10, borderWidth: 1, borderColor: C.border, padding: 14, marginBottom: 12 },
  h1: { color: C.text, fontSize: 22, fontWeight: '700', marginBottom: 12 },
  h2: { color: C.text, fontSize: 16, fontWeight: '600', marginBottom: 8 },
  text: { color: C.text, fontSize: 15 },
  muted: { color: C.muted, fontSize: 13 },
  mono: { color: C.text, fontFamily: 'monospace', fontSize: 13 },
  row: { flexDirection: 'row', alignItems: 'center', gap: 8, flexWrap: 'wrap' },
  input: { backgroundColor: C.bg, color: C.text, borderWidth: 1, borderColor: C.border, borderRadius: 8, padding: 12, fontSize: 15 },
  label: { color: C.muted, fontSize: 12, fontWeight: '600', marginBottom: 4, marginTop: 8 },
  bigValue: { color: C.text, fontSize: 34, fontWeight: '800' },
});

export function Button({ title, onPress, tone = 'default', disabled, style, small }: { title: string; onPress?: () => void; tone?: 'default' | 'primary' | 'danger' | 'ok'; disabled?: boolean; style?: ViewStyle; small?: boolean }) {
  const bg = tone === 'primary' ? C.primary : tone === 'danger' ? C.crit : tone === 'ok' ? C.ok : C.panel;
  return (
    <Pressable onPress={onPress} disabled={disabled} style={({ pressed }) => [{ backgroundColor: bg, opacity: disabled ? 0.5 : pressed ? 0.7 : 1, paddingVertical: small ? 8 : 14, paddingHorizontal: small ? 12 : 18, borderRadius: 10, borderWidth: 1, borderColor: tone === 'default' ? C.border : bg, alignItems: 'center' }, style]}>
      <Text style={{ color: C.text, fontWeight: '700', fontSize: small ? 13 : 16 }}>{title}</Text>
    </Pressable>
  );
}

export function Badge({ children, tone = 'default' }: { children: ReactNode; tone?: 'default' | 'ok' | 'warn' | 'crit' | 'info' }) {
  const color = tone === 'ok' ? C.ok : tone === 'warn' ? C.warn : tone === 'crit' ? C.crit : tone === 'info' ? C.info : C.muted;
  return <View style={{ backgroundColor: color + '33', borderRadius: 999, paddingHorizontal: 8, paddingVertical: 2 }}><Text style={{ color, fontSize: 12, fontWeight: '700' }}>{children}</Text></View>;
}

export function Field({ label, ...props }: TextInputProps & { label: string }) {
  return <View><Text style={s.label}>{label}</Text><TextInput placeholderTextColor={C.muted} style={s.input} {...props} /></View>;
}

export function Stat({ label, value, tone }: { label: string; value: ReactNode; tone?: 'ok' | 'warn' | 'crit' }) {
  const color = tone === 'ok' ? C.ok : tone === 'warn' ? C.warn : tone === 'crit' ? C.crit : C.text;
  return <View style={[s.panel, { flex: 1, minWidth: 100 }]}><Text style={s.muted}>{label}</Text><Text style={[s.bigValue, { color }]}>{value}</Text></View>;
}

export function Loading() { return <View style={{ padding: 24, alignItems: 'center' }}><ActivityIndicator color={C.primary} /></View>; }

/** Simple picker rendered as a list of chips (works everywhere without native pickers). */
export function Chips<T extends string>({ options, value, onChange }: { options: { value: T; label: string }[]; value: T | null | undefined; onChange: (v: T) => void }) {
  return <View style={[s.row, { marginVertical: 6 }]}>{options.map((o) => <Pressable key={o.value} onPress={() => onChange(o.value)} style={{ paddingVertical: 6, paddingHorizontal: 12, borderRadius: 999, borderWidth: 1, borderColor: value === o.value ? C.primary : C.border, backgroundColor: value === o.value ? C.primary : C.panel }}><Text style={{ color: C.text, fontSize: 13 }}>{o.label}</Text></Pressable>)}</View>;
}

export const rssiTone = (rssi?: number): 'ok' | 'warn' | 'crit' | 'default' => (rssi == null ? 'default' : rssi > -45 ? 'ok' : rssi > -60 ? 'warn' : 'crit');
