import { defaultEquipment, type ItemInput } from './items';
import type { PlayerPage } from './api';
export type MailInput = { title: string; body: string; recipientIds: number[]; items: ItemInput[]; gold: number };
export type MailDocument = { id: string; name: string; revision: number; input: MailInput };
export type MailRecipient = { characterId: number; name: string; nameDecodeStatus: string; nameBytesHex: string | null; level: number; job: number; growType: number };
export type MailPreview = { input: MailInput; displayText: string; utf8Bytes: number; recipients: MailRecipient[];
  items: { itemId: number; count: number; kind: string; durability: number; sealed: boolean; name?: string; upgradeLevel?: number; amplifyType?: number; amplifyValue?: number }[]; totalGold: number; totalItems: number };
export type MailReceipt = { requestId: string; administratorId: number; administratorName: string; committedAt: string; expiresAt: string; input: MailInput;
  recipients: { character: MailRecipient; accountId: number; mailId: number; equipmentUids: number[] }[]; items?: MailPreview['items'] | null };
export type MailSendResult = { receipt: MailReceipt; replayed: boolean; reminder: string };
export type PendingMail = { requestId: string; input: MailInput };
export type MailHistory = PlayerPage<MailReceipt>;
export const emptyMail = (): MailInput => ({ title: '', body: '', recipientIds: [], items: [], gold: 0 });
export function mergeMailItem(items: ItemInput[], item: ItemInput, editingIndex?: number): ItemInput[] {
  const source = editingIndex === undefined ? [...items, item] : items.map((old, index) => index === editingIndex ? item : old);
  const merged: ItemInput[] = []; const positions = new Map<string, number>();
  for (const row of source) {
    const equipment = row.equipment ?? defaultEquipment();
    const key = JSON.stringify([row.itemId, equipment.seal, equipment.growth, equipment.level, equipment.amplifyType]);
    const previous = positions.get(key);
    if (previous === undefined) { positions.set(key, merged.length); merged.push({ ...row }); }
    else {
      const count = merged[previous].count + row.count;
      if (!Number.isSafeInteger(count) || count > 2147483647) throw new Error('合并后的数量超出允许范围。');
      merged[previous] = { ...merged[previous], count };
    }
  }
  return merged;
}
export function readPending(key: string): PendingMail | null {
  try {
    const item = JSON.parse(sessionStorage.getItem(key) ?? 'null') as PendingMail | null;
    return item && typeof item.requestId === 'string' && item.input && typeof item.input.body === 'string'
      && typeof item.input.title === 'string' && Array.isArray(item.input.items) && Array.isArray(item.input.recipientIds) ? item : null;
  } catch { return null; }
}
