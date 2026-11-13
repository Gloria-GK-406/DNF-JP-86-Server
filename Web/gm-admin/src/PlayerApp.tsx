import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { Button, Spin } from 'antd';
import { MailOutlined, UnorderedListOutlined, UserOutlined } from '@ant-design/icons';
import { IconPackContext, ItemSourceContext, useIconPackStatus, type ItemSource } from './itemInfo';
import { useNotiBar } from './NotiBar';
import { ApiError } from './api';
import { codeOf, isRetainable, isUnexecuted, PLAYER_PREFIX, playerRequest, type PlayerIdentity, type PlayerMe } from './playerApi';
import { PlayerAccountView } from './PlayerAccountView';
import { PlayerMailView } from './PlayerMailView';
import { PlayerRecordsView } from './PlayerRecordsView';
import { Banner, BrandMark } from './ui';

/* The player's own console. Every read and write goes through `call`, which turns portal_closed into the closed page
   and unauthenticated into the login page before the caller sees the failure (SD-1a). */
type Pending<B> = { requestId: string; body: B; message: string };
/** A write's state, kept here so a result that may have committed survives page switches and remounts (RQ-012). */
type Slot = { pending: Pending<object> | null; busy: boolean };
type Player = { me?: PlayerMe; refreshMe: () => Promise<void>; setBalances: (balances: PlayerMe['balances']) => void;
  call: <T>(path: string, payload?: unknown) => Promise<T>; items: ItemSource;
  slots: Record<string, Slot>; submit: <B extends object, R>(path: string, body: B, accept: (result: R) => void) => Promise<void>; discard: (path: string) => void };
const PlayerContext = createContext<Player | null>(null);
export function usePlayer() { const value = useContext(PlayerContext); if (!value) throw new Error('usePlayer outside PlayerApp'); return value; }
/** Failures already handled by the shell: the page is about to go away. */
export const isRouted = (failure: unknown) => { const code = codeOf(failure); return code === 'portal_closed' || code === 'unauthenticated'; };

const pages = [
  { path: '/player', label: '我的账号', short: '账号', icon: <UserOutlined /> },
  { path: '/player/mail', label: '发邮件', short: '发邮件', icon: <MailOutlined /> },
  { path: '/player/records', label: '记录', short: '记录', icon: <UnorderedListOutlined /> },
];

export function PlayerApp({ identity, onClosed, onExpired, onSignedOut }: { identity: PlayerIdentity; onClosed: () => void; onExpired: () => void; onSignedOut: () => void }) {
  const location = useLocation(); const navigate = useNavigate(); const noti = useNotiBar();
  const exits = useRef({ onClosed, onExpired, onSignedOut }); exits.current = { onClosed, onExpired, onSignedOut };
  const gone = useRef(false); const alive = useRef(true);
  useEffect(() => { alive.current = true; gone.current = false; return () => { alive.current = false; gone.current = true; }; }, []);
  const leave = useCallback((exit: 'onClosed' | 'onExpired' | 'onSignedOut') => { if (gone.current) return; gone.current = true; exits.current[exit](); }, []);
  const call = useCallback(async <T,>(path: string, payload?: unknown) => {
    try { return await playerRequest<T>(path, payload); }
    catch (failure) {
      const code = codeOf(failure);
      if (code === 'portal_closed') leave('onClosed'); else if (code === 'unauthenticated') leave('onExpired');
      throw failure;
    }
  }, [leave]);
  const items = useMemo<ItemSource>(() => ({ key: 'player', prefix: PLAYER_PREFIX, read: call, quiet: <T,>(path: string) => call<T>(path).catch(() => undefined) }), [call]);
  const [me, setMe] = useState<PlayerMe>();
  const loaded = useRef(false);
  const [meError, setMeError] = useState<{ message: string; status: number }>();
  const [meReading, setMeReading] = useState(false);
  const refreshMe = useCallback(async () => {
    if (!loaded.current) setMeReading(true);
    try { const value = await call<PlayerMe>('me'); if (alive.current) { loaded.current = true; setMe(value); setMeError(undefined); } }
    catch (failure) {
      if (!alive.current || isRouted(failure)) return;
      // Before the first answer the page shows the failure in place of the console; afterwards a notice is enough.
      if (!loaded.current) setMeError({ message: failure instanceof Error ? failure.message : '读取失败', status: failure instanceof ApiError ? failure.status : 0 });
      else noti.failure(failure, 'player-me');
    } finally { if (alive.current) setMeReading(false); }
  }, [call, noti]);
  const [slots, setSlots] = useState<Record<string, Slot>>({});
  const running = useRef(new Set<string>());
  const patchSlot = useCallback((path: string, patch: Partial<Slot>) =>
    setSlots(old => ({ ...old, [path]: { ...(old[path] ?? { pending: null, busy: false }), ...patch } })), []);
  const pendingOf = useRef<Record<string, Pending<object> | null>>({});
  pendingOf.current = Object.fromEntries(Object.entries(slots).map(([path, slot]) => [path, slot.pending]));
  /** One write per path at a time. A result that may have committed keeps its request ID and content for a retry. */
  const submit = useCallback(async <B extends object, R>(path: string, body: B, accept: (result: R) => void) => {
    if (running.current.has(path)) return;
    const attempt = (pendingOf.current[path] as Pending<B> | null | undefined) ?? { requestId: crypto.randomUUID(), body, message: '' };
    running.current.add(path); patchSlot(path, { busy: true });
    try {
      const result = await call<R>(path, { requestId: attempt.requestId, ...attempt.body });
      if (!alive.current) return;
      patchSlot(path, { pending: null }); accept(result);
    } catch (failure) {
      if (!alive.current || isRouted(failure)) return;
      // Not run: the form unlocks for another submit, and an earlier unconfirmed attempt keeps its request ID.
      if (isUnexecuted(failure)) { noti.failure(failure); return; }
      if (isRetainable(failure)) { patchSlot(path, { pending: { ...attempt, message: failure instanceof Error ? failure.message : '' } }); return; }
      patchSlot(path, { pending: null });
      if (codeOf(failure) === 'feature_disabled') void refreshMe();
      noti.failure(failure);
    } finally { running.current.delete(path); if (alive.current) patchSlot(path, { busy: false }); }
  }, [call, noti, patchSlot, refreshMe]);
  const discard = useCallback((path: string) => { patchSlot(path, { pending: null }); void refreshMe(); }, [patchSlot, refreshMe]);
  const setBalances = useCallback((balances: PlayerMe['balances']) => setMe(old => old && { ...old, balances }), []);
  useEffect(() => { void refreshMe(); const timer = window.setInterval(() => void refreshMe(), 60000); return () => window.clearInterval(timer); }, [refreshMe]);
  const icons = useIconPackStatus(items, me?.portal.mailEnabled === true);
  const [leaving, setLeaving] = useState(false);
  async function logout() {
    setLeaving(true);
    try { await playerRequest<void>('auth/logout', {}); leave('onSignedOut'); }
    catch (failure) {
      const code = codeOf(failure);
      // A closed portal refuses the logout but has already ended every session; the page still leaves.
      if (code === 'unauthenticated') leave('onSignedOut'); else if (code === 'portal_closed') leave('onClosed'); else if (alive.current) noti.failure(failure);
    } finally { if (alive.current) setLeaving(false); }
  }
  const page = pages.find(item => item.path === location.pathname);
  if (!page) return <Navigate to="/player" replace />;
  const context: Player = { me, refreshMe, setBalances, call, items, slots, submit, discard };
  let content: ReactNode;
  if (!me && meError) content = <div className="denied" role="alert"><b>{meError.message}</b>
    {meError.status > 0 && <span className="mono small">HTTP {meError.status}</span>}
    <Button loading={meReading} onClick={() => void refreshMe()}>重新读取</Button></div>;
  else if (!me) content = <div className="boot"><Spin /></div>;
  else if (page.path === '/player') content = <PlayerAccountView key={identity.accountId} />;
  else if (page.path === '/player/mail') content = <PlayerMailView key={`${identity.accountId}:${location.key}`} />;
  else content = <PlayerRecordsView key={identity.accountId} />;
  return <PlayerContext.Provider value={context}><ItemSourceContext.Provider value={items}><IconPackContext.Provider value={icons}>
    <div className="app player">
      <nav className="rail" aria-label="玩家导航">
        <div className="rail-brand"><BrandMark /><b>86JP</b></div>
        <div className="rail-nav">{pages.map(item => <button key={item.path} className={`rail-item ${item === page ? 'on' : ''}`} aria-current={item === page ? 'page' : undefined}
          onClick={() => navigate(item.path)}><span className="ri-icon">{item.icon}</span><span>{item.label}</span></button>)}</div>
      </nav>
      <div className="main">
        <header className="topbar">
          <div className="crumbs"><b>{page.label}</b></div>
          <div className="top-right">
            <span className="who"><span className="avatar">{identity.loginName.slice(0, 1).toUpperCase()}</span><span className="hide-sm mono" style={{ fontSize: 13 }}>{identity.loginName}</span></span>
            <Button size="small" type="text" loading={leaving} onClick={() => void logout()}>退出</Button>
          </div>
        </header>
        <main className="content" key={location.pathname}>{content}</main>
        <nav className="tabbar" aria-label="底部导航">{pages.map(item => <button key={item.path} className={item === page ? 'on' : ''} aria-current={item === page ? 'page' : undefined}
          onClick={() => navigate(item.path)}>{item.icon}<span>{item.short}</span></button>)}</nav>
      </div>
    </div>
  </IconPackContext.Provider></ItemSourceContext.Provider></PlayerContext.Provider>;
}

export function useSubmission<B extends object, R>(path: string) {
  const { slots, submit, discard } = usePlayer();
  const slot = slots[path];
  return { pending: (slot?.pending ?? null) as Pending<B> | null, busy: slot?.busy ?? false,
    submit: (body: B, accept: (result: R) => void) => submit<B, R>(path, body, accept), discard: () => discard(path) };
}
export function PendingBanner({ pending, busy, onRetry, onDiscard }: { pending: Pending<object> | null; busy: boolean; onRetry: () => void; onDiscard: () => void }) {
  if (!pending) return null;
  return <Banner kind="crit" title="结果未确认" actions={<><Button size="small" danger loading={busy} onClick={onRetry}>用原编号重试</Button>
    <Button size="small" disabled={busy} onClick={onDiscard}>放弃</Button></>}>
    {pending.message} 编号 <span className="mono">{pending.requestId.slice(0, 8)}</span></Banner>;
}
