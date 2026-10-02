export type Permission = 'QueryPlayers' | 'SendMail' | 'GrantCera' | 'ViewAudit';
export type Identity = { id: number; loginName: string; permissions: Permission[]; expiresAt: string };
export type AuditRecord = { operationId: string; occurredAt: string; action: string; administratorName: string | null; target: string; outcome: string; reason: string };
export type AuditPage = { items: AuditRecord[]; total: number; page: number; pageSize: number };

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
