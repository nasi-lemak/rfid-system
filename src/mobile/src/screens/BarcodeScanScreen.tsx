import { useEffect, useRef, useState } from 'react';
import { Text, View } from 'react-native';
import { useNavigation, useRoute, type RouteProp } from '@react-navigation/native';
import { CameraView, useCameraPermissions } from 'expo-camera';
import type { RootStackParamList } from '../../App';
import { Button, C, s } from '../ui';

/**
 * Camera barcode / 2D fallback: scans Code128, GS1-128, EAN/UPC, QR and DataMatrix and hands the code back to the
 * calling screen through route.params.onScan (set by the caller) – hybrid RFID + barcode flows without a second device.
 */
export default function BarcodeScanScreen() {
  const nav = useNavigation();
  const route = useRoute<RouteProp<RootStackParamList, 'Barcode'>>();
  const [perm, requestPerm] = useCameraPermissions();
  const [last, setLast] = useState<string | null>(null);
  const [torch, setTorch] = useState(false);
  const busy = useRef(false);
  useEffect(() => { if (perm && !perm.granted && perm.canAskAgain) requestPerm(); }, [perm, requestPerm]);
  const onScanned = ({ data, type }: { data: string; type: string }) => {
    if (busy.current || !data) return;
    busy.current = true; setLast(`${type}: ${data}`);
    route.params?.onScan?.(data, type);
    if (route.params?.continuous) setTimeout(() => { busy.current = false; }, 1200); else nav.goBack();
  };
  if (!perm) return <View style={s.screen}><Text style={s.muted}>Checking camera permission…</Text></View>;
  if (!perm.granted) return <View style={s.screen}><Text style={s.text}>Camera access is needed for barcode scanning.</Text><Button title="Grant camera access" tone="primary" onPress={requestPerm} style={{ marginTop: 10 }} /></View>;
  return (
    <View style={{ flex: 1, backgroundColor: '#000' }}>
      <CameraView style={{ flex: 1 }} facing="back" enableTorch={torch} barcodeScannerSettings={{ barcodeTypes: ['code128', 'code39', 'ean13', 'ean8', 'upc_a', 'upc_e', 'qr', 'datamatrix', 'pdf417', 'itf14', 'codabar'] }} onBarcodeScanned={onScanned} />
      <View style={{ position: 'absolute', top: '30%', left: '10%', right: '10%', height: '30%', borderWidth: 2, borderColor: C.ok, borderRadius: 12 }} />
      <View style={{ padding: 12, backgroundColor: C.panel, gap: 8 }}>
        <Text style={s.text}>{route.params?.title ?? 'Scan a barcode or 2D code'}{route.params?.continuous ? ' · continuous' : ''}</Text>
        {last && <Text style={s.muted}>{last}</Text>}
        <View style={s.row}><Button small title={torch ? 'Torch off' : 'Torch on'} onPress={() => setTorch(!torch)} /><Button small title="Done" tone="primary" onPress={() => nav.goBack()} /></View>
      </View>
    </View>
  );
}
