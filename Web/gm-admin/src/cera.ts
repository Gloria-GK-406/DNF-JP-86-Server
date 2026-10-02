import type { Balances, PlayerPage } from './api';
export type CeraCurrency = 'Cera' | 'TokenCera' | 'HappyTokenCera';
export const currencyNames: Record<CeraCurrency, string> = { Cera: '点券', TokenCera: '代币点券', HappyTokenCera: '欢乐代币点券' };
export type CeraInput = { accountIds: number[]; currency: CeraCurrency; amount: number; reason: string };
export type CeraAccount = { accountId: number; loginName: string; balances: Balances };
export type CeraChange = { accountId: number; loginName: string; before: Balances; after: Balances };
export type CeraPreview = { input: CeraInput; accounts: CeraChange[]; totalAmount: number };
export type CeraReceipt = { requestId: string; administratorId: number; administratorName: string; committedAt: string; input: CeraInput; accounts: CeraChange[] };
export type CeraResult = { receipt: CeraReceipt; replayed: boolean; reminder: string };
export type PendingCera = { requestId: string; input: CeraInput };
export type CeraHistory = PlayerPage<CeraReceipt>;
export function readPendingCera(key: string): PendingCera | null {
  try {
    const value = JSON.parse(sessionStorage.getItem(key) ?? 'null') as PendingCera | null;
    if (!value || typeof value.requestId !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value.requestId)) return null;
    const input = value.input;
    return input && Array.isArray(input.accountIds) && input.accountIds.length >= 1 && input.accountIds.length <= 100
      && input.accountIds.every(x => Number.isInteger(x) && x > 0 && x <= 2147483647)
      && Object.hasOwn(currencyNames, input.currency) && Number.isInteger(input.amount) && input.amount > 0 && input.amount <= 2147483647
      && typeof input.reason === 'string' && input.reason.length <= 256 ? value : null;
  } catch { return null; }
}
