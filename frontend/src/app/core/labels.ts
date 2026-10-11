import { AssetAction, AssetCondition, AssetStatus, InventoryTransactionType, RequestStatus, RequestType } from './models';

export type Tone = 'success' | 'warning' | 'danger' | 'info' | 'primary' | 'violet' | 'rose' | 'neutral';

export interface Option<T extends string = string> {
  value: T;
  label: string;
}

const humanize = (value: string) => value.replace(/([a-z])([A-Z])/g, '$1 $2');

export const ASSET_STATUSES: Option<AssetStatus>[] = (['InStock', 'Assigned', 'UnderRepair', 'Damaged', 'Scrapped', 'Retired'] as AssetStatus[])
  .map(v => ({ value: v, label: humanize(v) }));

export const ASSET_CONDITIONS: Option<AssetCondition>[] = (['New', 'Good', 'Fair', 'Damaged', 'Critical'] as AssetCondition[])
  .map(v => ({ value: v, label: v }));

export const REQUEST_TYPES: Option<RequestType>[] = [
  { value: 'Repair', label: 'Repair' },
  { value: 'Replacement', label: 'Replacement' },
  { value: 'NewAsset', label: 'New Asset' },
  { value: 'Return', label: 'Return' },
  { value: 'Other', label: 'Other' }
];

export const REQUEST_STATUSES: Option<RequestStatus>[] = (['Submitted', 'Approved', 'Rejected', 'InProgress', 'Delivered', 'Checking', 'Received', 'Done'] as RequestStatus[])
  .map(v => ({ value: v, label: humanize(v) }));

/** The happy path, in order, for the status stepper. */
export const REQUEST_FLOW: RequestStatus[] = ['Submitted', 'Approved', 'InProgress', 'Delivered', 'Checking', 'Received', 'Done'];

export const ASSET_ACTIONS: Option<AssetAction>[] = (['Created', 'StockIn', 'Assigned', 'Returned', 'RepairRequested', 'RepairStarted', 'RepairCompleted',
  'ComponentReplaced', 'Damaged', 'Scrapped', 'Retired', 'StatusChanged', 'Updated'] as AssetAction[]).map(v => ({ value: v, label: humanize(v) }));

export const TRANSACTION_TYPES: Option<InventoryTransactionType>[] = (['StockIn', 'StockOut', 'Return', 'Scrap', 'Adjustment'] as InventoryTransactionType[])
  .map(v => ({ value: v, label: humanize(v) }));

export function label(value: string | null | undefined): string {
  if (!value) return '—';
  if (value === 'NewAsset') return 'New Asset';
  return humanize(value);
}

export function statusTone(status: string | null | undefined): Tone {
  switch (status) {
    case 'InStock': case 'Done': case 'Received': case 'Active': case 'Success': case 'New': case 'Good': case 'StockIn': case 'Return':
      return 'success';
    case 'Assigned': case 'Approved': case 'Delivered': case 'StockOut':
      return 'primary';
    case 'UnderRepair': case 'InProgress': case 'Fair': case 'Warning': case 'Adjustment':
      return 'warning';
    case 'Damaged': case 'Rejected': case 'Critical': case 'Failure': case 'Error': case 'Scrap': case 'Scrapped':
      return 'danger';
    case 'Checking': case 'Information':
      return 'info';
    case 'Submitted':
      return 'violet';
    default:
      return 'neutral';
  }
}

/** Icon + tone used on timelines for each history action. */
export function actionVisual(action: AssetAction): { icon: string; tone: Tone } {
  switch (action) {
    case 'Created': case 'StockIn': return { icon: 'package-plus', tone: 'success' };
    case 'Assigned': return { icon: 'user-check', tone: 'primary' };
    case 'Returned': return { icon: 'undo', tone: 'info' };
    case 'RepairRequested': return { icon: 'message', tone: 'violet' };
    case 'RepairStarted': return { icon: 'wrench', tone: 'warning' };
    case 'RepairCompleted': return { icon: 'check-circle', tone: 'success' };
    case 'ComponentReplaced': return { icon: 'cpu', tone: 'violet' };
    case 'Damaged': return { icon: 'alert', tone: 'danger' };
    case 'Scrapped': return { icon: 'trash', tone: 'danger' };
    case 'Retired': return { icon: 'archive', tone: 'neutral' };
    default: return { icon: 'edit', tone: 'neutral' };
  }
}

export function actionSentence(h: { action: AssetAction; employeeName: string | null; assetTag: string; assetName: string }): string {
  const who = h.employeeName ? ` ${h.employeeName}` : '';
  switch (h.action) {
    case 'Assigned': return `${h.assetName} assigned to${who || ' an employee'}`;
    case 'Returned': return `${h.assetName} returned${who ? ' by' + who : ''}`;
    case 'StockIn': return `${h.assetName} received into stock`;
    case 'Created': return `${h.assetName} registered`;
    case 'RepairRequested': return `Repair requested for ${h.assetName}`;
    case 'RepairStarted': return `${h.assetName} sent to repair`;
    case 'RepairCompleted': return `${h.assetName} repaired`;
    case 'ComponentReplaced': return `Component replaced on ${h.assetName}`;
    case 'Damaged': return `${h.assetName} marked damaged`;
    case 'Scrapped': return `${h.assetName} moved to scrap`;
    case 'Retired': return `${h.assetName} retired`;
    default: return `${h.assetName} ${label(h.action).toLowerCase()}`;
  }
}

export function initials(name: string | null | undefined): string {
  if (!name) return '?';
  const parts = name.trim().split(/\s+/).filter(Boolean);
  return ((parts[0]?.[0] ?? '') + (parts.length > 1 ? parts[parts.length - 1][0] : '')).toUpperCase();
}

/** Stable avatar colour per person. */
export function avatarClass(name: string | null | undefined): string {
  let hash = 0;
  for (const ch of name ?? '') hash = (hash * 31 + ch.charCodeAt(0)) >>> 0;
  return `c${hash % 6}`;
}
