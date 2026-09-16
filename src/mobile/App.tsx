import { useCallback, useEffect, useMemo, useState } from 'react';
import { StatusBar } from 'expo-status-bar';
import { DarkTheme, NavigationContainer } from '@react-navigation/native';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { ReaderProvider } from './src/reader';
import { SettingsContext, defaultSettings, getSettings, saveSettings, type Settings } from './src/store/settings';
import LoginScreen from './src/screens/LoginScreen';
import HomeScreen from './src/screens/HomeScreen';
import InventoryScreen from './src/screens/InventoryScreen';
import LookupScreen from './src/screens/LookupScreen';
import LocateScreen from './src/screens/LocateScreen';
import StocktakeScreen from './src/screens/StocktakeScreen';
import OperationScreen from './src/screens/OperationScreen';
import CommissionScreen from './src/screens/CommissionScreen';
import SettingsScreen from './src/screens/SettingsScreen';
import FloorPlanScreen from './src/screens/FloorPlanScreen';
import GeofenceScreen from './src/screens/GeofenceScreen';
import { LangContext, translate } from './src/i18n';
import { C } from './src/ui';

export type RootStackParamList = {
  Home: undefined;
  Inventory: undefined;
  Lookup: { epc?: string } | undefined;
  Locate: { epc?: string; name?: string } | undefined;
  Stocktake: undefined;
  Operation: { epcs?: string[]; type?: string } | undefined;
  Commission: { epc?: string } | undefined;
  FloorPlan: { locationId?: string; itemId?: string } | undefined;
  Geofences: undefined;
  Settings: undefined;
};

const Stack = createNativeStackNavigator<RootStackParamList>();
const theme = { ...DarkTheme, colors: { ...DarkTheme.colors, background: C.bg, card: C.panel, text: C.text, border: C.border, primary: C.primary } };

export default function App() {
  const [settings, setSettings] = useState<Settings>(defaultSettings);
  const [loaded, setLoaded] = useState(false);
  useEffect(() => { getSettings().then((s) => { setSettings(s); setLoaded(true); }); }, []);
  const update = useCallback(async (patch: Partial<Settings>) => setSettings(await saveSettings(patch)), []);
  const ctx = useMemo(() => ({ settings, update, loaded }), [settings, update, loaded]);
  return (
    <SafeAreaProvider>
      <SettingsContext.Provider value={ctx}>
        <LangContext.Provider value={settings.language ?? 'en'}>
        <StatusBar style="light" />
        {!loaded ? null : !settings.token ? <LoginScreen /> : (
          <ReaderProvider>
            <NavigationContainer theme={theme}>
              <Stack.Navigator screenOptions={{ headerStyle: { backgroundColor: C.panel }, headerTintColor: C.text }}>
                <Stack.Screen name="Home" component={HomeScreen} options={{ title: translate(settings.language ?? 'en', 'RFID Handheld') }} />
                <Stack.Screen name="Inventory" component={InventoryScreen} options={{ title: 'Scan / Inventory' }} />
                <Stack.Screen name="Lookup" component={LookupScreen} />
                <Stack.Screen name="Locate" component={LocateScreen} />
                <Stack.Screen name="Stocktake" component={StocktakeScreen} />
                <Stack.Screen name="Operation" component={OperationScreen} options={{ title: 'Operation' }} />
                <Stack.Screen name="Commission" component={CommissionScreen} options={{ title: 'Commission tags' }} />
                <Stack.Screen name="FloorPlan" component={FloorPlanScreen} options={{ title: translate(settings.language ?? 'en', 'Floor plan') }} />
                <Stack.Screen name="Geofences" component={GeofenceScreen} options={{ title: translate(settings.language ?? 'en', 'Map & geofences') }} />
                <Stack.Screen name="Settings" component={SettingsScreen} />
              </Stack.Navigator>
            </NavigationContainer>
          </ReaderProvider>
        )}
        </LangContext.Provider>
      </SettingsContext.Provider>
    </SafeAreaProvider>
  );
}
