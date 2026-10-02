export type Permission = 'QueryPlayers' | 'SendMail' | 'GrantCera' | 'ViewAudit';
export type Identity = { id: number; loginName: string; permissions: Permission[]; expiresAt: string };
export type AuditRecord = { operationId: string; occurredAt: string; action: string; administratorName: string | null; target: string; outcome: string; reason: string };
export type AuditPage = { items: AuditRecord[]; total: number; page: number; pageSize: number };
export type Balances = { cera: number; tokenCera: number; happyTokenCera: number };
export type PlayerPage<T> = { items: T[]; total: number; page: number; pageSize: number; sampledAt: string };
export type AccountView = { accountId: number; loginName: string; balances: Balances; characterCount: number; isOnline: boolean; activeCharacterId: number | null; sampledAt: string };
export type CharacterView = { characterId: number; accountId: number; loginName: string; name: string; nameDecodeStatus: string; nameBytesHex: string | null;
  job: number; growType: number; level: number; isDeleted: boolean; isOnline: boolean; channel: { channelId: number; name: string } | null;
  position: { source: string; townId: number | null; areaId: number | null; x: number | null; y: number | null; direction: number | null }; sampledAt: string };
export type CharacterDetail = CharacterView & { balances: Balances };
export type Overview = { networkEnabled: boolean; gameReady: boolean; totalAccounts: number; totalCharacters: number; onlineAccounts: number; onlineCharacters: number;
  channels: { channelId: number; name: string; onlineCharacters: number }[]; unmappedOnlineCharacters: number; sampledAt: string };

export class ApiError extends Error { constructor(public status: number, message: string) { super(message); } }

export async function api<T>(path: string, payload?: unknown): Promise<T> {
  const headers: Record<string, string> = {};
  if (payload !== undefined) {
    const response = await fetch('/api/gm/v1/auth/csrf', { credentials: 'same-origin' });
    if (!response.ok) throw new ApiError(response.status, '请求验证失败，请刷新页面。');
    headers['X-GM-CSRF'] = (await response.json()).token;
    headers['Content-Type'] = 'application/json';
  }
  const response = await fetch(`/api/gm/v1/${path}`, { credentials: 'same-origin', method: payload === undefined ? 'GET' : 'POST', headers,
    body: payload === undefined ? undefined : JSON.stringify(payload) });
  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new ApiError(response.status, body.message ?? '服务暂不可用，请稍后重试。');
  }
  return response.status === 204 ? undefined as T : response.json();
}
