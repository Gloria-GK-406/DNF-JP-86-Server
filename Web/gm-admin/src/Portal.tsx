import { useEffect, useRef, useState } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { Spin } from 'antd';
import { AdminApp } from './AdminApp';
import { useNotiBar } from './NotiBar';
import { PlayerApp } from './PlayerApp';
import { codeOf, playerRequest, type PlayerIdentity } from './playerApi';
import { PortalClosedView } from './PortalClosedView';

/* Top-level shell: the player portal status and session decide between the player console, the closed page and the
   administrator console (whose login page offers the player choice while the portal is open). A valid player session
   wins over an administrator session in the same browser. */
type State = { phase: 'probing' } | { phase: 'player'; identity: PlayerIdentity } | { phase: 'entry'; enabled: boolean } | { phase: 'closed' };
const playerPath = /^\/player(\/|$)/;

async function portalOpen() {
  try { return (await playerRequest<{ enabled: boolean }>('auth/status')).enabled === true; } catch { return false; }
}

export function Portal() {
  const noti = useNotiBar(); const navigate = useNavigate(); const location = useLocation();
  const [state, setState] = useState<State>({ phase: 'probing' });
  const [busy, setBusy] = useState(false);
  const epoch = useRef(0); const fresh = useRef(false); const mounted = useRef(true);
  useEffect(() => { mounted.current = true; void probe(); return () => { mounted.current = false; epoch.current++; }; }, []);
  async function probe() {
    const current = ++epoch.current;
    let enabled = await portalOpen();
    if (!mounted.current || epoch.current !== current) return;
    if (enabled) {
      try {
        const identity = await playerRequest<PlayerIdentity>('auth/session');
        if (mounted.current && epoch.current === current) setState({ phase: 'player', identity });
        return;
      } catch (failure) {
        if (!mounted.current || epoch.current !== current) return;
        if (codeOf(failure) === 'portal_closed') enabled = false;
      }
    }
    fresh.current = true; setState({ phase: 'entry', enabled });
  }
  /** The login page re-reads the status whenever it shows, except right after the probe that led to it. */
  async function shown() {
    if (fresh.current) { fresh.current = false; return; }
    const current = epoch.current; const enabled = await portalOpen();
    if (mounted.current && epoch.current === current) setState(old => old.phase === 'entry' ? { phase: 'entry', enabled } : old);
  }
  async function login(values: { loginName: string; password: string }) {
    const current = ++epoch.current; setBusy(true);
    try {
      const identity = await playerRequest<PlayerIdentity>('auth/login', values);
      if (!mounted.current || epoch.current !== current) return;
      noti.clear(); setState({ phase: 'player', identity }); navigate('/player');
    } catch (failure) {
      if (!mounted.current || epoch.current !== current) return;
      // A credential refusal is unauthenticated too: here it is the answer to this form, not an expired session.
      if (codeOf(failure) === 'portal_closed') { noti.clear(); setState({ phase: 'closed' }); } else noti.failure(failure);
    } finally { if (mounted.current && epoch.current === current) setBusy(false); }
  }
  function leave(next: 'closed' | 'expired' | 'out') {
    epoch.current++; noti.clear(); fresh.current = false;
    if (next === 'expired') noti.warning('会话已失效，请重新登录。', { key: 'session-expired', persistent: true });
    setState(next === 'closed' ? { phase: 'closed' } : { phase: 'entry', enabled: true });
    navigate('/');
  }
  if (state.phase === 'probing') return <div className="boot"><Spin /></div>;
  if (state.phase === 'player') return <PlayerApp key={state.identity.accountId} identity={state.identity}
    onClosed={() => leave('closed')} onExpired={() => leave('expired')} onSignedOut={() => leave('out')} />;
  if (state.phase === 'closed') return <PortalClosedView onBack={() => { setState({ phase: 'probing' }); void probe(); }} />;
  if (playerPath.test(location.pathname)) return <Navigate to="/" replace />;
  return <AdminApp player={{ enabled: state.enabled, busy, onLogin: values => void login(values), onShown: () => void shown() }} />;
}
