import { useEffect, useState } from 'react';
import { FlatList, Pressable, Text, View } from 'react-native';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import type { RootStackParamList } from '../../App';
import { Api, type WorkflowRow } from '../api/client';
import { Badge, Loading, s } from '../ui';

/** Guided workflows configured for the tenant (templates + custom); cached so they open offline. */
export default function WorkflowsScreen() {
  const nav = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const [rows, setRows] = useState<WorkflowRow[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { Api.workflows().then((w) => setRows(w.filter((x) => x.enabled))).catch((e) => { setError((e as Error).message); setRows([]); }); }, []);
  if (rows === null) return <Loading />;
  return (
    <View style={s.screen}>
      {error && <Text style={s.muted}>{error}</Text>}
      {rows.length === 0 && <Text style={s.muted}>No workflows configured. Install a solution template or define one in the web app (Configure → Workflows).</Text>}
      <FlatList data={rows} keyExtractor={(w) => w.id} renderItem={({ item: w }) => (
        <Pressable onPress={() => nav.navigate('WorkflowRun', { code: w.code })} style={[s.panel, { marginBottom: 8 }]}>
          <View style={[s.row, { justifyContent: 'space-between' }]}><Text style={s.h2}>{w.icon ? `${w.icon} ` : ''}{w.name}</Text><Badge>{w.steps.length} steps</Badge></View>
          {!!w.description && <Text style={s.muted}>{w.description}</Text>}
          <Text style={[s.muted, { marginTop: 4 }]}>{w.steps.map((st, i) => `${i + 1}. ${st.title || st.key}`).join('  ›  ')}</Text>
          {w.itemTypeCodes.length > 0 && <Text style={s.muted}>for {w.itemTypeCodes.join(', ')}</Text>}
        </Pressable>
      )} />
    </View>
  );
}
