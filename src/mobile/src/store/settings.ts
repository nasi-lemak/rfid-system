import AsyncStorage from '@react-native-async-storage/async-storage';
import { createContext, useContext } from 'react';
import type { ReaderKind } from '../reader/types';

export interface Settings {
  serverUrl: string;
  token: string | null;
  userName: string | null;
  deviceId: string | null;
  readerKind: ReaderKind;
  currentLocationId: string | null;
  currentLocationName: string | null;
  power: number;
  beep: boolean;
  printerDeviceId: string | null;
  printerDeviceName: string | null;
  gpsEnabled: boolean;
  language: 'en' | 'ms' | 'zh' | 'es' | 'de' | 'fr';
  /** Set when the server rejected the stored token (401): the login screen explains why the operator is back there. */
  sessionExpired: boolean;
}

export const defaultSettings: Settings = { serverUrl: 'http://10.0.2.2:5080', token: null, userName: null, deviceId: null, readerKind: 'simulated', currentLocationId: null, currentLocationName: null, power: 30, beep: true, printerDeviceId: null, printerDeviceName: null, gpsEnabled: false, language: 'en', sessionExpired: false };
const KEY = 'rfid.settings';
let cache: Settings | null = null;

export async function getSettings(): Promise<Settings> {
  if (cache) return cache;
  try { const raw = await AsyncStorage.getItem(KEY); cache = raw ? { ...defaultSettings, ...JSON.parse(raw) } : { ...defaultSettings }; } catch { cache = { ...defaultSettings }; }
  return cache!;
}
export async function saveSettings(patch: Partial<Settings>): Promise<Settings> {
  const next = { ...(await getSettings()), ...patch };
  cache = next;
  try { await AsyncStorage.setItem(KEY, JSON.stringify(next)); } catch { /* ignore */ }
  return next;
}

export interface SettingsCtx { settings: Settings; update: (patch: Partial<Settings>) => Promise<void>; loaded: boolean }
export const SettingsContext = createContext<SettingsCtx>({ settings: defaultSettings, update: async () => {}, loaded: false });
export const useSettings = () => useContext(SettingsContext);
