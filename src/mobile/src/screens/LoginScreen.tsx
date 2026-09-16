import { useEffect, useState } from 'react';
import { KeyboardAvoidingView, Platform, ScrollView, Text, View } from 'react-native';
import { Api } from '../api/client';
import { readQueue } from '../store/queue';
import { useSettings } from '../store/settings';
import { Button, Field, s, C } from '../ui';

// Demo accounts are offered only in development builds; a pilot device must never show them.
const DEMO = __DEV__ ? { email: 'operator@demo.local', password: 'operator123' } : { email: '', password: '' };

export default function LoginScreen() {
  const { settings, update } = useSettings();
  const [serverUrl, setServerUrl] = useState(settings.serverUrl);
  const [email, setEmail] = useState(DEMO.email);
  const [password, setPassword] = useState(DEMO.password);
  const [deviceToken, setDeviceToken] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [pending, setPending] = useState(0);
  useEffect(() => { readQueue().then((q) => setPending(q.length)).catch(() => {}); }, []);

  const login = async () => {
    setBusy(true); setError(null);
    try {
      await update({ serverUrl: serverUrl.trim() });
      if (deviceToken.trim()) { const r = await Api.deviceLogin(deviceToken.trim()); await update({ token: r.token, userName: r.device.name, deviceId: r.device.id, sessionExpired: false }); }
      else { const r = await Api.login(email.trim().toLowerCase(), password); await update({ token: r.token, userName: r.user.displayName, sessionExpired: false }); }
    } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  };
  return (
    <KeyboardAvoidingView style={{ flex: 1, backgroundColor: C.bg }} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
      <ScrollView contentContainerStyle={{ padding: 20, justifyContent: 'center', flexGrow: 1 }}>
        <Text style={[s.h1, { textAlign: 'center' }]}>RFID Handheld</Text>
        {settings.sessionExpired && (
          <View style={[s.panel, { borderColor: C.warn }]}>
            <Text style={[s.text, { fontWeight: '700' }]}>Your session has expired – sign in again</Text>
            {pending > 0 && <Text style={s.muted}>{pending} operation(s) are waiting on this device and will sync automatically after you sign in. Nothing has been lost.</Text>}
          </View>
        )}
        <View style={s.panel}>
          <Field label="Server URL" value={serverUrl} onChangeText={setServerUrl} autoCapitalize="none" keyboardType="url" placeholder="http://10.0.2.2:5080" />
          <Field label="Email" value={email} onChangeText={setEmail} autoCapitalize="none" keyboardType="email-address" />
          <Field label="Password" value={password} onChangeText={setPassword} secureTextEntry />
          <Text style={[s.muted, { marginTop: 12 }]}>— or provision with a device token (Admin → Readers & devices) —</Text>
          <Field label="Device token" value={deviceToken} onChangeText={setDeviceToken} autoCapitalize="none" placeholder="optional" />
          {error && <Text style={{ color: C.crit, marginTop: 8 }}>{error}</Text>}
          <Button title={busy ? 'Signing in…' : 'Sign in'} tone="primary" onPress={login} disabled={busy} style={{ marginTop: 16 }} />
        </View>
        {__DEV__ && <Text style={[s.muted, { textAlign: 'center' }]}>Android emulator reaches your PC at 10.0.2.2 · real devices use the PC's LAN IP</Text>}
      </ScrollView>
    </KeyboardAvoidingView>
  );
}
