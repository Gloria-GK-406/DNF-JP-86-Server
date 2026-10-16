import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { Button, Drawer, Input, Popover, Segmented, Tooltip, type InputRef } from 'antd';
import { AuditOutlined, BgColorsOutlined, CalendarOutlined, DashboardOutlined, ExclamationCircleOutlined, GiftOutlined, LogoutOutlined, MenuOutlined, SearchOutlined, SendOutlined, SettingOutlined, TeamOutlined } from '@ant-design/icons';
import { api, ApiError, type Identity, type Overview, type Permission, type ServerInfo } from './api';
import { greeting } from './format';
import { quietApi } from './quietApi';
import { IconPackContext, useIconPackStatus } from './itemInfo';
import { palettes, useLook, type Mode, type PaletteKey } from './look';
import { useNotiBar } from './NotiBar';
import { GrantsProvider, usePendingCount } from './pending';
import { BrandMark, Denied, KV } from './ui';
import { LoginView } from './LoginView';
import { OverviewView } from './OverviewView';
import { PlayersView } from './PlayersView';
import { GrantsView } from './GrantsView';
import { MailComposer, type MailSeed } from './MailComposer';
import { CeraComposer, type CeraSeed } from './CeraComposer';
import { InitializationView } from './InitializationView';
import { DailyRefillView } from './DailyRefillView';
import { EventsView } from './EventsView';
import { AuditView } from './AuditView';

type Need = Permission | 'All' | 'Grants';
type Page = { key: string; label: string; group: string; icon: ReactNode; need: Need };
const pages: Page[] = [
  { key: '/', label: '服务概览', group: '运营', icon: <DashboardOutlined />, need: 'QueryPlayers' },
  { key: '/players', label: '账号与角色', group: '运营', icon: <TeamOutlined />, need: 'QueryPlayers' },
  { key: '/grants', label: '发放', group: '运营', icon: <SendOutlined />, need: 'Grants' },
  { key: '/initialization', label: '初始化配置', group: '游戏配置', icon: <SettingOutlined />, need: 'All' },
  { key: '/daily-refill', label: '每日补给', group: '游戏配置', icon: <GiftOutlined />, need: 'All' },
  { key: '/events', label: '活动管理', group: '游戏配置', icon: <CalendarOutlined />, need: 'All' },
  { key: '/operations', label: '操作记录', group: '审计', icon: <AuditOutlined />, need: 'ViewAudit' },
];
const subpages: Record<string, { label: string; parent: string; need: Permission }> = {
  '/grants/mail': { label: '写邮件', parent: '/grants', need: 'SendMail' }, '/grants/cera': { label: '发点券', parent: '/grants', need: 'GrantCera' },
};
const legacy: Record<string, string> = { '/characters': '/players', '/mail': '/grants', '/wallet': '/grants' };
const permissionNames: Record<Permission, string> = { QueryPlayers: '查询', SendMail: '邮件', GrantCera: '点券', ViewAudit: '审计' };

/** Navigation shared by pages: composers are full pages that take an optional seed. */
type Nav = { go: (path: string, state?: unknown) => void; composeMail: (seed?: MailSeed) => void; composeCera: (seed?: CeraSeed) => void; can: (need: Need) => boolean };
const NavContext = createContext<Nav>({ go: () => {}, composeMail: () => {}, composeCera: () => {}, can: () => false });
export const useNav = () => useContext(NavContext);

export function AdminApp() {
  const [identity, setIdentity] = useState<Identity | null>(null);
  const [initialized, setInitialized] = useState<boolean | null>(null);
  const [loading, setLoading] = useState(true);
  const noti = useNotiBar();
  const [configurationDenied, setConfigurationDenied] = useState(false);
  const hasPermission = (value: Identity, need: Need) => need === 'All'
    ? !configurationDenied && (['QueryPlayers', 'SendMail', 'GrantCera', 'ViewAudit'] as Permission[]).every(p => value.permissions.includes(p))
    : need === 'Grants' ? value.permissions.includes('SendMail') || value.permissions.includes('GrantCera') : value.permissions.includes(need);
  const generation = useRef(0);
  const mounted = useRef(true);
  const transitioning = useRef(false);
  const currentIdentity = useRef<Identity | null>(null);
  const location = useLocation(); const navigate = useNavigate();
  function isCurrent(epoch: number) { return mounted.current && generation.current === epoch; }
  function applyIdentity(value: Identity | null) { if (currentIdentity.current?.id !== value?.id) noti.clear(); currentIdentity.current = value; setIdentity(value); }
  function beginTransition() { transitioning.current = true; return ++generation.current; }
  function expire(epoch: number) {
    if (!isCurrent(epoch) || transitioning.current) return;
    generation.current++; applyIdentity(null); noti.warning('会话已失效，请重新登录。', { key: 'session-expired', persistent: true });
  }
  function denyPermission(epoch: number, permission: Permission) {
    if (!isCurrent(epoch) || transitioning.current || !currentIdentity.current) return;
    generation.current++;
    applyIdentity({ ...currentIdentity.current, permissions: currentIdentity.current.permissions.filter(value => value !== permission) });
  }
  async function refresh() {
    const epoch = beginTransition();
    setLoading(true);
    try {
      const status = await api<{ initialized: boolean }>('auth/status');
      if (!isCurrent(epoch)) return;
      setInitialized(status.initialized);
      if (!status.initialized) { applyIdentity(null); return; }
      try { const current = await api<Identity>('auth/session'); if (isCurrent(epoch)) applyIdentity(current); }
      catch (failure) { if (!isCurrent(epoch)) return; if (failure instanceof ApiError && failure.status === 401) applyIdentity(null); else throw failure; }
    } catch (failure) { if (isCurrent(epoch)) noti.failure(failure); }
    finally { if (isCurrent(epoch)) { transitioning.current = false; setLoading(false); } }
  }
  useEffect(() => {
    mounted.current = true; void refresh();
    return () => { mounted.current = false; generation.current++; };
  }, []);
  useEffect(() => {
    if (!identity) return;
    let active = true;
    const timer = window.setInterval(() => {
      if (!active || transitioning.current || currentIdentity.current?.id !== identity.id) return;
      const epoch = generation.current;
      api<Identity>('auth/session').then(current => { if (active && isCurrent(epoch)) applyIdentity(current); }).catch(failure => {
        if (active && failure instanceof ApiError && failure.status === 401) expire(epoch);
      });
    }, 60000);
    return () => { active = false; window.clearInterval(timer); };
  }, [identity?.id]);
  async function login(values: { loginName: string; password: string }) {
    const epoch = beginTransition();
    setConfigurationDenied(false);
    setLoading(true);
    try {
      const current = await api<Identity>('auth/login', values);
      if (!isCurrent(epoch)) return;
      applyIdentity(current); navigate(pages.find(page => hasPermission(current, page.need))?.key ?? '/');
    } catch (failure) { if (isCurrent(epoch)) noti.failure(failure); }
    finally { if (isCurrent(epoch)) { transitioning.current = false; setLoading(false); } }
  }
  async function logout() {
    const epoch = beginTransition(); setLoading(true);
    try { await api<void>('auth/logout', {}); if (isCurrent(epoch)) applyIdentity(null); }
    catch (failure) {
      if (!isCurrent(epoch)) return;
      if (failure instanceof ApiError && failure.status === 401) applyIdentity(null); else noti.failure(failure);
    } finally { if (isCurrent(epoch)) { transitioning.current = false; setLoading(false); } }
  }
  if (!identity) return <LoginView initialized={initialized} loading={loading} onLogin={values => void login(values)} onReconnect={() => void refresh()} />;
  if (legacy[location.pathname]) return <Navigate to={legacy[location.pathname]} replace />;
  const viewGeneration = generation.current;
  const access = { onExpired: () => expire(viewGeneration) };
  const configDenied = () => { if (isCurrent(viewGeneration)) setConfigurationDenied(true); };
  const deny = (permission: Permission) => () => denyPermission(viewGeneration, permission);
  const sub = subpages[location.pathname];
  const page = pages.find(item => item.key === (sub?.parent ?? location.pathname));
  const allowed = page !== undefined && hasPermission(identity, page.need) && (!sub || hasPermission(identity, sub.need));
  const key = `${identity.id}:${viewGeneration}`;
  const state = location.state as { seed?: unknown; q?: string } | null;
  let content: ReactNode;
  if (!page) content = <Denied title="没有这个页面">地址可能写错了，从左侧导航进入吧。</Denied>;
  else if (!allowed) content = <Denied />;
  else if (location.pathname === '/') content = <OverviewView key={key} {...access} onDenied={deny('QueryPlayers')} />;
  else if (location.pathname === '/players') content = <PlayersView key={`${key}:${state?.q ?? ''}:${location.key}`} initialQuery={state?.q} {...access} onDenied={deny('QueryPlayers')} />;
  else if (location.pathname === '/grants') content = <GrantsView key={key} {...access} onDeniedMail={deny('SendMail')} onDeniedCera={deny('GrantCera')} />;
  else if (location.pathname === '/grants/mail') content = <MailComposer key={`${key}:${location.key}`} seed={state?.seed as MailSeed | undefined} {...access} onDenied={deny('SendMail')} />;
  else if (location.pathname === '/grants/cera') content = <CeraComposer key={`${key}:${location.key}`} seed={state?.seed as CeraSeed | undefined} {...access} onDenied={deny('GrantCera')} />;
  else if (location.pathname === '/initialization') content = <InitializationView key={key} {...access} onDenied={configDenied} />;
  else if (location.pathname === '/daily-refill') content = <DailyRefillView key={key} {...access} onDenied={configDenied} />;
  else if (location.pathname === '/events') content = <EventsView key={key} {...access} onDenied={configDenied} />;
  else content = <AuditView key={key} {...access} onDenied={deny('ViewAudit')} />;
  const nav: Nav = {
    go: (path, navState) => navigate(path, navState === undefined ? undefined : { state: navState }),
    composeMail: seed => navigate('/grants/mail', { state: { seed } }), composeCera: seed => navigate('/grants/cera', { state: { seed } }),
    can: need => hasPermission(identity, need),
  };
  return <GrantsProvider key={identity.id} administratorId={identity.id} onExpired={access.onExpired} onDenied={permission => denyPermission(viewGeneration, permission)}>
    <NavContext.Provider value={nav}>
      <Shell identity={identity} page={page} sub={sub?.label} loggingOut={loading} onLogout={() => void logout()}>{content}</Shell>
    </NavContext.Provider>
  </GrantsProvider>;
}

function Shell({ identity, page, sub, loggingOut, onLogout, children }: { identity: Identity; page?: Page; sub?: string; loggingOut: boolean; onLogout: () => void; children: ReactNode }) {
  const nav = useNav(); const location = useLocation();
  const [mobileNav, setMobileNav] = useState(false);
  const [search, setSearch] = useState('');
  const searchRef = useRef<InputRef>(null);
  const pending = usePendingCount();
  const icons = useIconPackStatus();
  const [info, setInfo] = useState<ServerInfo>(); const [overview, setOverview] = useState<Overview>();
  const canQuery = nav.can('QueryPlayers');
  useEffect(() => {
    let active = true;
    quietApi<ServerInfo>('server-info').then(value => { if (active && value && Array.isArray(value.gamePorts)) setInfo(value); });
    const read = () => { if (canQuery) quietApi<Overview>('overview').then(value => { if (active && typeof value?.onlineCharacters === 'number') setOverview(value); }); };
    read(); const timer = canQuery ? window.setInterval(read, 90000) : undefined;
    return () => { active = false; window.clearInterval(timer); };
  }, [canQuery]);
  useEffect(() => {
    const key = (event: KeyboardEvent) => { if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') { event.preventDefault(); searchRef.current?.focus(); } };
    window.addEventListener('keydown', key); return () => window.removeEventListener('keydown', key);
  }, []);
  useEffect(() => { setMobileNav(false); }, [location.pathname]);
  function doSearch() { const q = search.trim(); if (q) nav.go('/players', { q }); }
  const rail = <Rail current={page?.key} pending={pending} info={info} overview={overview} />;
  return <IconPackContext.Provider value={icons}>
    <div className="app">
      {rail}
      <div className="main">
        <header className="topbar">
          <Button className="menu-btn" type="text" aria-label="打开导航" icon={<MenuOutlined />} onClick={() => setMobileNav(true)} />
          <div className="crumbs">{page ? <><span>{page.group}</span><span>/</span><b>{page.label}</b>{sub && <><span>/</span><b>{sub}</b></>}</> : <b>管理台</b>}</div>
          {canQuery && <Input ref={searchRef} className="search" allowClear prefix={<SearchOutlined />} suffix={<kbd>Ctrl K</kbd>} aria-label="全局搜索"
            placeholder="搜角色名、角色 ID、账号…" value={search} onChange={event => setSearch(event.target.value)} onPressEnter={doSearch} />}
          <div className="top-right">
            {pending > 0 && <Button size="small" danger icon={<ExclamationCircleOutlined />} onClick={() => nav.go('/grants')}>{pending} 件待确认</Button>}
            <span className="greet hide-sm">{greeting()}，{identity.loginName}</span>
            <Popover trigger="click" placement="bottomRight" content={<LookPanel />}>
              <Button type="text" size="small" aria-label="外观与色系" icon={<BgColorsOutlined />} /></Popover>
            <Popover trigger="click" placement="bottomRight" content={<WhoPanel identity={identity} loggingOut={loggingOut} onLogout={onLogout} />}>
              <button className="who" aria-label="账号与会话"><span className="avatar">{identity.loginName.slice(0, 1).toUpperCase()}</span>
                <span className="hide-sm" style={{ fontSize: 13 }}>{identity.loginName}</span></button></Popover>
          </div>
        </header>
        <main className="content" key={location.pathname}>{children}</main>
      </div>
    </div>
    <Drawer rootClassName="drawer-rail" placement="left" size={240} open={mobileNav} onClose={() => setMobileNav(false)} closable={false}>{rail}</Drawer>
  </IconPackContext.Provider>;
}

function Rail({ current, pending, info, overview }: { current?: string; pending: number; info?: ServerInfo; overview?: Overview }) {
  const nav = useNav();
  const visible = pages.filter(page => nav.can(page.need));
  const groups = [...new Set(visible.map(page => page.group))];
  const ready = overview ? overview.networkEnabled && overview.gameReady : info?.networkEnabled;
  return <nav className="rail" aria-label="主导航">
    <div className="rail-brand"><BrandMark /><b>86JP 管理台</b></div>
    <div className="rail-nav">{groups.map(group => <div key={group}><div className="rail-label">{group}</div>
      {visible.filter(page => page.group === group).map(page => { const on = current === page.key;
        const meta = page.key === '/players' && overview ? String(overview.onlineCharacters) : undefined;
        const attn = page.key === '/grants' && pending > 0 ? pending : 0;
        return <button key={page.key} className={`rail-item ${on ? 'on' : ''}`} aria-current={on ? 'page' : undefined} onClick={() => nav.go(page.key)}>
          <span className="ri-icon">{page.icon}</span><span>{page.label}</span>{attn ? <span className="ri-meta attn">{attn}</span> : meta && <span className="ri-meta" aria-label={`在线 ${meta}`}>{meta}</span>}
        </button>; })}
    </div>)}</div>
    <div className="rail-tape" aria-label="服务状态">
      {ready === undefined ? <div><span className="led off" />状态未知</div> : <div><span className={`led ${ready ? '' : 'off'}`} /><b>{ready ? '游戏服务就绪' : overview && !overview.networkEnabled ? '游戏网络已关闭' : '游戏服务未就绪'}</b></div>}
      {info && <div>CH <b>{info.channelPort ?? '—'}</b> · GAME <b>{info.gamePorts.join('/') || '—'}</b></div>}
      {info && <div>{info.pvfSha256 ? <>PVF <b>{info.pvfSha256.slice(0, 8).toUpperCase()}</b> · </> : null}v{info.version}</div>}
      <div className={`rail-mood ${pending ? 'attn' : ''}`}>{pending ? `有 ${pending} 件事等你处理` : '阿拉德今夜无事'}</div>
    </div>
  </nav>;
}

function LookPanel() {
  const look = useLook();
  return <div className="pop">
    <div><h4>外观</h4><Segmented block size="small" value={look.mode} onChange={value => look.setMode(value as Mode)}
      options={[{ label: '跟随系统', value: 'system' }, { label: '浅色', value: 'light' }, { label: '深色', value: 'dark' }]} /></div>
    <div><h4>色系</h4><div className="swatches" role="radiogroup" aria-label="色系">{(Object.keys(palettes) as PaletteKey[]).map(key =>
      <button key={key} role="radio" aria-checked={look.palette === key} className={`swatch ${look.palette === key ? 'on' : ''}`} onClick={() => look.setPalette(key)}>
        <i style={{ background: palettes[key].swatch }} />{palettes[key].name}</button>)}</div></div>
    <span className="faint" style={{ fontSize: 11.5 }}>成功、警告、危险这些状态色不跟着色系变。</span>
  </div>;
}

function WhoPanel({ identity, loggingOut, onLogout }: { identity: Identity; loggingOut: boolean; onLogout: () => void }) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => { const timer = window.setInterval(() => setNow(Date.now()), 30000); return () => window.clearInterval(timer); }, []);
  const left = Math.max(0, new Date(identity.expiresAt).getTime() - now);
  const hours = Math.floor(left / 3600000); const minutes = Math.floor(left % 3600000 / 60000);
  return <div className="who-pop">
    <KV rows={[['管理员', identity.loginName], ['会话剩余', <span className="mono" key="left">{hours}:{String(minutes).padStart(2, '0')} · 到点需重新登录</span>],
      ['权限', identity.permissions.length ? identity.permissions.map(p => permissionNames[p]).join(' · ') : '无']]} />
    <Tooltip title="会话固定 8 小时，不自动续期"><Button danger block icon={<LogoutOutlined />} loading={loggingOut} onClick={onLogout}>退出登录</Button></Tooltip>
  </div>;
}
