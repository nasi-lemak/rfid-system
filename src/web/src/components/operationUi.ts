import type { OperationDefinition } from '../api/types';

/** Which inputs an operation form needs, derived from the definition's requirements and effects. */
export interface OperationUi {
  needsTo: boolean; showTo: boolean; needsParty: boolean; showParty: boolean; showDueBack: boolean;
  needsContainer: boolean; showState: boolean; needsState: boolean; needsQuantity: boolean; showQuantity: boolean; isCommission: boolean;
}

const has = (d: OperationDefinition, kind: string) => d.effects.some((e) => e.kind === kind);

export function uiFor(d: OperationDefinition): OperationUi {
  const r = d.requires;
  return {
    needsTo: r.toLocation, showTo: r.toLocation || has(d, 'Move'),
    needsParty: r.party, showParty: r.party || has(d, 'SetCustodian'),
    showDueBack: has(d, 'SetDueBack'),
    needsContainer: r.container || has(d, 'Pack'),
    showState: r.targetState || has(d, 'SetState'), needsState: r.targetState,
    needsQuantity: r.quantity, showQuantity: r.quantity || has(d, 'AdjustQuantity'),
    isCommission: d.baseType === 'Commission',
  };
}

/** Built-in shapes used until the definitions request resolves (and by very old servers that lack it). */
const builtIn: Record<string, Partial<OperationDefinition['requires']> & { effects: string[]; description: string }> = {
  Receive: { toLocation: true, effects: ['Activate', 'Move'], description: 'Items arrive at a location (goods-in, returns from customer, sample intake).' },
  Transfer: { toLocation: true, effects: ['Move', 'SetCustodian'], description: 'Move items to another location; containers carry their contents.' },
  Dispatch: { toLocation: true, effects: ['Move', 'SetCustodian', 'SetDueBack'], description: 'Ship to a customer or external location (sale, rental, keg delivery).' },
  Issue: { party: true, effects: ['SetCustodian', 'SetDueBack', 'Move'], description: 'Hand items to a person / department / customer (checkout, loan, assignment).' },
  Return: { effects: ['ClearCustodian', 'ClearDueBack', 'Move'], description: 'Take items back into stock, clearing custody.' },
  Count: { effects: ['RecordSeen'], description: 'Confirm presence without moving (cycle count).' },
  Inspect: { effects: ['RecordInspection', 'SetState'], description: 'Record an inspection; Target state = result (e.g. Passed / Failed).' },
  Maintain: { effects: ['Move', 'SetState'], description: 'Send for maintenance / repair.' },
  Dispose: { effects: ['Dispose'], description: 'Retire permanently; tags are released.' },
  Pack: { container: true, effects: ['Pack'], description: 'Put items inside a container item (pallet, tray, kit, cage).' },
  Unpack: { effects: ['Unpack', 'Move'], description: 'Remove items from their container.' },
  ProcessStage: { effects: ['SetState', 'Move'], description: 'Advance items through their lifecycle (wash stage, sterilisation, production step).' },
  Commission: { effects: [], description: 'Bind a new EPC to a new or existing item.' },
  Adjust: { quantity: true, effects: ['AdjustQuantity'], description: 'Change quantity by a delta (consumption, receipt of bulk stock).' },
};

export function fallbackDefinition(code: string): OperationDefinition {
  const b = builtIn[code] ?? { effects: [], description: '' };
  return {
    id: code, code, name: code === 'ProcessStage' ? 'Process stage' : code, description: b.description, baseType: code, eventType: 'Seen', isBuiltIn: true, enabled: true, itemTypeCodes: [],
    effects: b.effects.map((kind) => ({ kind, params: {} })),
    requires: { toLocation: !!b.toLocation, party: !!b.party, container: !!b.container, targetState: false, quantity: !!b.quantity, fromStates: [] },
  };
}

export const builtInCodes = Object.keys(builtIn);
