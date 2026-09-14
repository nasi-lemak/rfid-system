import { useState } from 'react';
import { KeyboardAvoidingView, Platform, ScrollView, Text, View } from 'react-native';
import { Api } from '../api/client';
import { useSettings } from '../store/settings';
import { Button, Field, s, C } from '../ui';

export default function LoginScreen() {
  const { settings, update } = useSettings();
  const [serverUrl, setServerUrl] = useState(settings.serverUrl);
  const [email, setEmail] = useState('operator@demo.local');
  const [password, setPassword] = useState('operator123');
  const [deviceToken, setDeviceToken] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const login = async () => {
    setBusy(true); setError(null);
    try {
      await update({ serverUrl: serverUrl.trim() });
      if (deviceToken.trim()) { const r = await Api.deviceLogin(deviceToken.trim()); await update({ token: r.token, userName: r.device.name, deviceId: r.device.id }); }
      else { const r = await Api.login(email.trim().toLowerCase(), password); await update({ token: r.token, userName: r.user.displayName }); }
    } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  };
  return (
    <KeyboardAvoidingView style={{ flex: 1, backgroundColor: C.bg }} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
      <ScrollView contentContainerStyle={{ padding: 20, justifyContent: 'center', flexGrow: 1 }}>
        <Text style={[s.h1, { textAlign: 'center' }]}>RFID Handheld</Text>
        <View style={s.panel}>
          <Field label="Server URL" value={serverUrl} onChangeText={setServerUrl} autoCapitalize="none" keyboardType="url" placeholder="http://10.0.2.2:5080" />
          <Field label="Email" value={email} onChangeText={setEmail} autoCapitalize="none" keyboardType="email-address" />
          <Field label="Password" value={password} onChangeText={setPassword} secureTextEntry />
          <Text style={[s.muted, { marginTop: 12 }]}>— or provision with a device token (Admin → Readers & devices) —</Text>
          <Field label="Device token" value={deviceToken} onChangeText={setDeviceToken} autoCapitalize="none" placeholder="optional" />
          {error && <Text style={{ color: C.crit, marginTop: 8 }}>{error}</Text>}
          <Button title={busy ? 'Signing in…' : 'Sign in'} tone="primary" onPress={login} disabled={busy} style={{ marginTop: 16 }} />
        </View>
        <Text style={[s.muted, { textAlign: 'center' }]}>Android emulator reaches your PC at 10.0.2.2 · real devices use the PC's LAN IP</Text>
      </ScrollView>
    </KeyboardAvoidingView>
  );
}
