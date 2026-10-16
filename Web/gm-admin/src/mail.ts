import type { ItemInput } from './items';
import type { PlayerPage } from './api';
export type MailInput = { title: string; body: string; recipientIds: number[]; items: ItemInput[]; gold: number };
export type MailDocument = { id: string; name: string; revision: number; input: MailInput };
export type MailRecipient = { characterId: number; name: string; nameDecodeStatus: string; nameBytesHex: string | null; level: number; job: number; growType: number };
export type MailPreviewItem = { itemId: number; count: number; kind: string; durability: number; sealed: boolean; name?: string; upgradeLevel?: number;
  amplifyType?: number; amplifyValue?: number; forgeLevel?: number; socketCount?: number; quality?: number };
export type MailPreview = { input: MailInput; displayText: string; utf8Bytes: number; recipients: MailRecipient[]; items: MailPreviewItem[]; totalGold: number; totalItems: number };
export type MailReceipt = { requestId: string; administratorId: number; administratorName: string; committedAt: string; expiresAt: string; input: MailInput;
  recipients: { character: MailRecipient; accountId: number; mailId: number; equipmentUids: number[] }[]; items?: MailPreviewItem[] | null };
export type MailSendResult = { receipt: MailReceipt; replayed: boolean; reminder: string };
export type PendingMail = { requestId: string; input: MailInput };
export type MailHistory = PlayerPage<MailReceipt>;
export const emptyMail = (): MailInput => ({ title: '', body: '', recipientIds: [], items: [], gold: 0 });
export function readPending(key: string): PendingMail | null {
  try {
    const item = JSON.parse(sessionStorage.getItem(key) ?? 'null') as PendingMail | null;
    return item && typeof item.requestId === 'string' && item.input && typeof item.input.body === 'string'
      && typeof item.input.title === 'string' && Array.isArray(item.input.items) && Array.isArray(item.input.recipientIds) ? item : null;
  } catch { return null; }
}
