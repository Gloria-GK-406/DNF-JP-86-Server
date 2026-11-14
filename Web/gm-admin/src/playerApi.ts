import { ApiError } from './api';
import { failureMessage } from './NotiBar';

/* The player API (/api/player/v1). Failures carry `code` (SD-1a) and callers branch on it alone. A 5xx without a code
   (an unexpected server failure) and a request that never got an answer are both `unknown`: a write may have committed. */
export const PLAYER_PREFIX = '/api/player/v1';
export const RATE_LIMITED = '请求过于频繁，请稍后重试。';

export type PlayerIdentity = { accountId: number; loginName: string; expiresAt: string };
export type PlayerLimits = { ceraPerGrant: number; ceraPerDay: number; tokenCeraPerGrant: number; tokenCeraPerDay: number; mailGoldPerMail: number; mailsPerDay: number };
export type PlayerCharacter = { characterId: number; name: string; job: number; level: number; isOnline: boolean };
export type PlayerMe = { accountId: number; loginName: string; balances: { cera: number; tokenCera: number }; characters: PlayerCharacter[];
  portal: { ceraEnabled: boolean; mailEnabled: boolean; limits: PlayerLimits; today: { cera: number; tokenCera: number; mails: number } } };
export type PlayerCurrency = 'Cera' | 'TokenCera';
export type PlayerReceipt = { requestId: string; kind: 'cera' | 'mail'; committedAt: string; summary: string; result: string;
  currency?: PlayerCurrency; amount?: number; before?: { cera: number; tokenCera: number }; after?: { cera: number; tokenCera: number };
  characterId?: number; characterName?: string; title?: string; body?: string; items?: { itemId: number; count: number; name: string | null; kind: string }[];
  gold?: number; expiresAt?: string };
export type PlayerCeraResult = { receipt: PlayerReceipt; replayed: boolean; reminder: string };
export type PlayerMailResult = { receipt: PlayerReceipt; replayed: boolean };
export type PlayerOperationsPage = { items: PlayerReceipt[]; total: number; page: number; pageSize: number };
export type PlayerPortalSettings = { enabled: boolean; ceraEnabled: boolean; mailEnabled: boolean; limits: PlayerLimits; revision: number };
export type PlayerPortalSessionEntry = { accountId: number; loginName: string; createdAt: string; expiresAt: string };

async function send(path: string, init: RequestInit) {
  try { return await fetch(`${PLAYER_PREFIX}/${path}`, { credentials: 'same-origin', signal: AbortSignal.timeout(30000), ...init }); }
  catch (failure) { throw new ApiError(0, failure instanceof TypeError ? '无法连接服务，请检查网络后重试。' : failureMessage(failure), 'unknown', 'unknown'); }
}
async function refusal(response: Response, path?: string) {
  const body = await response.json().catch(() => ({})) as { message?: unknown; code?: unknown; outcome?: unknown };
  const code = typeof body.code === 'string' ? body.code : response.status >= 500 ? 'unknown' : 'invalid';
  // The login keeps its own wording; elsewhere rate_limited is the per-account cap on requests in flight (not run).
  const message = code === 'rate_limited' && path !== 'auth/login' ? RATE_LIMITED : typeof body.message === 'string' ? body.message : '服务暂不可用，请稍后重试。';
  return new ApiError(response.status, message, typeof body.outcome === 'string' ? body.outcome : undefined, code);
}

export async function playerRequest<T>(path: string, payload?: unknown): Promise<T> {
  const headers: Record<string, string> = {};
  if (payload !== undefined) {
    const csrf = await send('auth/csrf', {});
    if (!csrf.ok) throw await refusal(csrf);
    headers['X-GM-CSRF'] = (await csrf.json()).token;
    headers['Content-Type'] = 'application/json';
  }
  const response = await send(path, { method: payload === undefined ? 'GET' : 'POST', headers, body: payload === undefined ? undefined : JSON.stringify(payload) });
  if (!response.ok) throw await refusal(response, path);
  return response.status === 204 ? undefined as T : response.json();
}

export const codeOf = (failure: unknown) => failure instanceof ApiError ? failure.code : undefined;
/** The request was refused before it ran: it says nothing about an earlier attempt that may have committed. */
export const isUnexecuted = (failure: unknown) => codeOf(failure) === 'rate_limited';
/** Whether a write may have committed: keep its request ID and content for a retry. */
export function isRetainable(failure: unknown) {
  if (!(failure instanceof ApiError)) return true;
  return failure.code === 'unknown' || failure.outcome === 'unknown' || failure.outcome === 'rolledBack';
}
