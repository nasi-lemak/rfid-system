import { NativeModules } from 'react-native';
import { Api } from '../api/client';
import { getSettings } from '../store/settings';

/**
 * Label printing from the handheld. Two paths:
 *  - network: the server renders ZPL and sends it to the selected Printer device (Config.host:port)
 *  - bluetooth: the app fetches the ZPL and sends it to a paired mobile printer (Zebra ZQ630R/ZQ520,
 *    Honeywell RP) through the "BtPrinter" native module { print(zpl: string): Promise<void> }.
 * The Bluetooth path degrades to a clear error when the native module is not linked.
 */
export async function printLabel(itemId: string): Promise<string> {
  const s = await getSettings();
  if (!s.printerDeviceId) throw new Error('No printer selected (Settings → Printer)');
  if (s.printerDeviceId === 'bluetooth') {
    const mod = (NativeModules as Record<string, { print?: (zpl: string) => Promise<void> }>).BtPrinter;
    if (!mod?.print) throw new Error('Bluetooth printer module is not linked in this build; select a network printer instead.');
    const zpl = await Api.labelZpl(itemId);
    await mod.print(zpl);
    return 'Label sent to Bluetooth printer';
  }
  const r = await Api.printLabel([itemId], s.printerDeviceId);
  if (!r[0]?.ok) throw new Error(r[0]?.error ?? 'Print failed');
  return `Label sent to ${s.printerDeviceName ?? 'printer'} (EPC ${r[0].epc})`;
}
