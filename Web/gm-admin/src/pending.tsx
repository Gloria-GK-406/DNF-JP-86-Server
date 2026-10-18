import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { api, ApiError, type Permission } from './api';
import { useNotiBar } from './NotiBar';
import { readPending, type MailReceipt, type MailSendResult, type PendingMail } from './mail';
import { readPendingCera, type CeraReceipt, type CeraResult, type PendingCera } from './cera';

/* Batch grants (official mail, cera) share one recovery rule: the request ID and frozen input are written to sessionStorage
   before the request leaves; an unconfirmed result keeps them until the operator queries the original ID or retries the
   original content. Never mint a new ID for the same grant. The provider outlives the composer pages so the shell, the
   overview and the grants page all see the same pending state. */
export type GrantOutcome = 'committed' | 'rejected' | 'unknown';
type Kind = 'mail' | 'cera';
type Grants = {
  mail: PendingMail | null; cera: PendingCera | null; busy: Record<Kind, boolean>; committed: number;
  sendMail: (snapshot: PendingMail) => Promise<{ outcome: GrantOutcome; result?: MailSendResult }>;
  grantCera: (snapshot: PendingCera) => Promise<{ outcome: GrantOutcome; result?: CeraResult }>;
  queryMail: () => Promise<MailReceipt | undefined>;
  queryCera: () => Promise<CeraReceipt | undefined>;
};
const GrantsContext = createContext<Grants | null>(null);
export function useGrants() { const value = useContext(GrantsContext); if (!value) throw new Error('GrantsProvider missing'); return value; }
export const mailPendingKey = (administratorId: number) => `dfo.gm.mail.pending.${administratorId}`;
export const ceraPendingKey = (administratorId: number) => `dfo.gm.cera.pending.${administratorId}`;

export function GrantsProvider({ administratorId, onExpired, onDenied, children }: {
  administratorId: number; onExpired: () => void; onDenied: (permission: Permission) => void; children: ReactNode;
}) {
  const noti = useNotiBar();
  const keys = { mail: mailPendingKey(administratorId), cera: ceraPendingKey(administratorId) };
  const [mail, setMail] = useState<PendingMail | null>(() => readPending(keys.mail));
  const [cera, setCera] = useState<PendingCera | null>(() => readPendingCera(keys.cera));
  const [busy, setBusy] = useState<Record<Kind, boolean>>({ mail: false, cera: false });
  const [committed, setCommitted] = useState(0);
  const alive = useRef(true); const generation = useRef({ mail: 0, cera: 0 }); const running = useRef({ mail: false, cera: false });
  const access = useRef({ onExpired, onDenied }); access.current = { onExpired, onDenied };
  useEffect(() => { alive.current = true; return () => { alive.current = false; generation.current.mail++; generation.current.cera++; }; }, []);
  const setPending = (kind: Kind, value: PendingMail | PendingCera | null) => kind === 'mail' ? setMail(value as PendingMail | null) : setCera(value as PendingCera | null);
  function forget(kind: Kind) { setPending(kind, null); try { sessionStorage.removeItem(keys[kind]); } catch { /* A receipt remains available from the server. */ } }
  function refused(kind: Kind, failure: unknown) {
    if (failure instanceof ApiError && failure.status === 401) access.current.onExpired();
    else if (failure instanceof ApiError && failure.status === 403) access.current.onDenied(kind === 'mail' ? 'SendMail' : 'GrantCera');
    else noti.failure(failure);
  }
  function begin(kind: Kind) { running.current[kind] = true; setBusy(old => ({ ...old, [kind]: true })); return ++generation.current[kind]; }
  function finish(kind: Kind, epoch: number) { if (alive.current && epoch === generation.current[kind]) { running.current[kind] = false; setBusy(old => ({ ...old, [kind]: false })); } }
  const current = (kind: Kind, epoch: number) => alive.current && epoch === generation.current[kind];

  async function submit<T extends { replayed: boolean }>(kind: Kind, snapshot: PendingMail | PendingCera, path: string): Promise<{ outcome: GrantOutcome; result?: T }> {
    if (running.current[kind]) return { outcome: 'unknown' };
    const epoch = begin(kind);
    try { sessionStorage.setItem(keys[kind], JSON.stringify(snapshot)); }
    catch { noti.error('浏览器无法保存待确认请求，请启用会话存储后重试。'); finish(kind, epoch); return { outcome: 'rejected' }; }
    setPending(kind, snapshot);
    try {
      const result = await api<T>(path, { ...snapshot.input, requestId: snapshot.requestId });
      if (!current(kind, epoch)) return { outcome: 'unknown' };
      forget(kind); setCommitted(value => value + 1);
      noti.success(kind === 'mail' ? (result.replayed ? '已恢复原发送结果，没有重复发放。' : '整批邮件已提交。') : (result.replayed ? '已恢复原发放结果，没有重复到账。' : '整批点券已到账。'));
      return { outcome: 'committed', result };
    } catch (failure) {
      if (!current(kind, epoch)) return { outcome: 'unknown' };
      if (failure instanceof ApiError && (failure.status === 401 || failure.status === 403)) { refused(kind, failure); return { outcome: 'unknown' }; }
      if (failure instanceof ApiError && failure.outcome !== 'unknown' && (failure.status < 500 || failure.outcome === 'rolledBack' || failure.outcome === 'rejected')) {
        forget(kind); refused(kind, failure); return { outcome: 'rejected' };
      }
      return { outcome: 'unknown' };
    } finally { finish(kind, epoch); }
  }
  async function query<T>(kind: Kind, pending: PendingMail | PendingCera | null): Promise<T | undefined> {
    if (!pending || running.current[kind]) return undefined;
    const epoch = begin(kind);
    try {
      const receipt = await api<T>(`${kind}/operations/${pending.requestId}`);
      if (!current(kind, epoch)) return undefined;
      forget(kind); setCommitted(value => value + 1);
      noti.success(kind === 'mail' ? '已找到持久化发送结果。' : '已找到持久化发放结果。');
      return receipt;
    } catch (failure) { if (current(kind, epoch)) refused(kind, failure); return undefined; }
    finally { finish(kind, epoch); }
  }
  const value: Grants = {
    mail, cera, busy, committed,
    sendMail: snapshot => submit<MailSendResult>('mail', snapshot, 'mail/send'),
    grantCera: snapshot => submit<CeraResult>('cera', snapshot, 'cera/grant'),
    queryMail: () => query<MailReceipt>('mail', mail),
    queryCera: () => query<CeraReceipt>('cera', cera),
  };
  return <GrantsContext.Provider value={value}>{children}</GrantsContext.Provider>;
}
export const usePendingCount = () => { const grants = useGrants(); return useMemo(() => (grants.mail ? 1 : 0) + (grants.cera ? 1 : 0), [grants.mail, grants.cera]); };
