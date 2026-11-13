import { useEffect, useRef, useState } from 'react';
import { Button, InputNumber, Popconfirm, Switch, Table } from 'antd';
import { LockOutlined, ReloadOutlined } from '@ant-design/icons';
import { api, ApiError } from './api';
import { bjTime, numberFormat } from './format';
import { useNotiBar } from './NotiBar';
import type { PlayerLimits, PlayerPortalSessionEntry, PlayerPortalSettings } from './playerApi';
import { Banner, Field, PageHead, Panel } from './ui';

/* 玩家后台：开关切换即提交（限额取已保存的值），限额点"保存"才提交；两者都带读到的 revision。
   冲突或结果未确认时锁定写入，重新读取后才能继续。 */
type Access = { onExpired: () => void; onDenied: () => void };
const limitFields: [keyof PlayerLimits, string][] = [['ceraPerGrant', '点券单次'], ['ceraPerDay', '点券每日'], ['tokenCeraPerGrant', '代币券单次'],
  ['tokenCeraPerDay', '代币券每日'], ['mailGoldPerMail', '邮件金币单封'], ['mailsPerDay', '邮件每日封数']];
type Draft = Record<keyof PlayerLimits, number | null>;
type Switches = Pick<PlayerPortalSettings, 'enabled' | 'ceraEnabled' | 'mailEnabled'>;

export function PlayerPortalAdminView({ onExpired, onDenied }: Access) {
  const noti = useNotiBar();
  const [settings, setSettings] = useState<PlayerPortalSettings>();
  const [draft, setDraft] = useState<Draft>();
  const [sessions, setSessions] = useState<PlayerPortalSessionEntry[]>();
  const [lock, setLock] = useState<'conflict' | 'unknown'>();
  const [busy, setBusy] = useState(false); const [reading, setReading] = useState(false); const [kicking, setKicking] = useState(false);
  const alive = useRef(true); const writing = useRef(false);
  useEffect(() => { alive.current = true; void reload(); return () => { alive.current = false; }; }, []);
  function routed(failure: unknown) {
    if (failure instanceof ApiError && failure.status === 401) { onExpired(); return true; }
    if (failure instanceof ApiError && failure.status === 403) { onDenied(); return true; }
    return false;
  }
  async function readSessions() {
    try { const value = await api<PlayerPortalSessionEntry[]>('player-portal/sessions'); if (alive.current) setSessions(value); }
    catch (failure) { if (alive.current && !routed(failure)) noti.failure(failure, 'player-portal-sessions'); }
  }
  async function reload() {
    setReading(true);
    try {
      const value = await api<PlayerPortalSettings>('player-portal/settings');
      if (!alive.current) return;
      setSettings(value); setDraft({ ...value.limits }); setLock(undefined);
    } catch (failure) { if (alive.current && !routed(failure)) noti.failure(failure, 'player-portal-settings'); }
    finally { if (alive.current) setReading(false); }
    await readSessions();
  }
  async function save(switches: Switches, limits: PlayerLimits, fromDraft: boolean) {
    if (!settings || writing.current || lock) return;
    writing.current = true; setBusy(true);
    try {
      const value = await api<PlayerPortalSettings>('player-portal/settings', { ...switches, limits, revision: settings.revision });
      if (!alive.current) return;
      setSettings(value);
      if (fromDraft) { setDraft({ ...value.limits }); noti.success('已保存'); }
      if (settings.enabled && !value.enabled) void readSessions();
    } catch (failure) {
      if (!alive.current || routed(failure)) return;
      if (failure instanceof ApiError && failure.status === 409) setLock('conflict');
      else if (!(failure instanceof ApiError) || failure.status >= 500) { setLock('unknown'); noti.failure(failure); }
      else noti.failure(failure);
    } finally { writing.current = false; if (alive.current) setBusy(false); }
  }
  async function kick(accountId: number | null) {
    setKicking(true);
    try {
      const value = await api<{ kicked: number }>(accountId === null ? 'player-portal/sessions/kick-all' : 'player-portal/sessions/kick', accountId === null ? {} : { accountId });
      if (!alive.current) return;
      noti.success(`已踢下线 ${value.kicked} 个会话`);
      await readSessions();
    } catch (failure) { if (alive.current && !routed(failure)) noti.failure(failure); }
    finally { if (alive.current) setKicking(false); }
  }
  const locked = !!lock || busy || !settings;
  const valid = !!draft && limitFields.every(([key]) => draft[key] !== null);
  const dirty = !!draft && !!settings && limitFields.some(([key]) => draft[key] !== settings.limits[key]);
  const toggle = (patch: Partial<Switches>) => settings && void save({ enabled: settings.enabled, ceraEnabled: settings.ceraEnabled, mailEnabled: settings.mailEnabled, ...patch }, settings.limits, false);
  const switchRow = (name: string, value: boolean | undefined, patch: (on: boolean) => Partial<Switches>, kind: 'master' | 'sub') =>
    <div className={`sw-row ${kind} ${kind === 'sub' && settings && !settings.enabled ? 'muted' : ''}`}><span className="name">{name}</span>
      <Switch aria-label={name} checked={!!value} disabled={locked} loading={busy && !!settings} onChange={on => toggle(patch(on))} /></div>;
  return <div className="page">
    <PageHead title="玩家后台"><Button size="small" icon={<ReloadOutlined />} loading={reading} disabled={busy} onClick={() => void reload()}>重新读取</Button></PageHead>
    {lock && <Banner kind="crit" icon={<LockOutlined />} title={lock === 'conflict' ? '设置已被修改' : '结果未确认'}
      actions={<Button size="small" danger loading={reading} onClick={() => void reload()}>重新读取</Button>} />}
    <div className="grid g-2">
      <div className="grid">
        <section className="panel" aria-label="开关">
          {switchRow('玩家后台', settings?.enabled, on => ({ enabled: on }), 'master')}
          {switchRow('加点券', settings?.ceraEnabled, on => ({ ceraEnabled: on }), 'sub')}
          {switchRow('发邮件', settings?.mailEnabled, on => ({ mailEnabled: on }), 'sub')}
        </section>
        <Panel title="限额" count="0 = 不限" foot={<><span className="grow" />
          <Button type="primary" disabled={locked || !dirty || !valid} loading={busy} onClick={() => settings && draft && void save({ enabled: settings.enabled, ceraEnabled: settings.ceraEnabled, mailEnabled: settings.mailEnabled }, draft as PlayerLimits, true)}>保存</Button></>}>
          <div className="fgrid">{limitFields.map(([key, label]) => <Field key={key} label={label} htmlFor={`limit-${key}`}>
            <InputNumber<number> id={`limit-${key}`} min={0} max={2147483647} precision={0} style={{ width: '100%' }} disabled={locked} {...numberFormat}
              value={draft?.[key] ?? null} onChange={value => setDraft(old => old && { ...old, [key]: value })} /></Field>)}</div>
        </Panel>
      </div>
      <Panel title="在线会话" count={sessions?.length ?? ''} flush tools={<>
        <Button size="small" type="text" aria-label="刷新会话" icon={<ReloadOutlined />} onClick={() => void readSessions()} />
        <Popconfirm title="全部踢下线？" okText="全部踢下线" cancelText="取消" okButtonProps={{ danger: true }} onConfirm={() => kick(null)}>
          <Button size="small" danger disabled={kicking || !sessions?.length}>全部踢下线</Button></Popconfirm></>}>
        <Table<PlayerPortalSessionEntry> size="small" rowKey="accountId" dataSource={sessions ?? []} loading={sessions === undefined} pagination={false} scroll={{ x: 460 }}
          locale={{ emptyText: '无' }} columns={[
            { title: '账号', key: 'account', render: (_, r) => <span><span className="mono">{r.loginName}</span> <span className="id">{r.accountId}</span></span> },
            { title: '登录于', dataIndex: 'createdAt', render: v => <span className="mono small">{bjTime(v)}</span> },
            { title: '到期', dataIndex: 'expiresAt', render: v => <span className="mono small">{bjTime(v)}</span> },
            { title: '', key: 'kick', align: 'right', render: (_, r) => <Button size="small" disabled={kicking} aria-label={`踢下线 ${r.loginName}`} onClick={() => void kick(r.accountId)}>踢下线</Button> },
          ]} />
      </Panel>
    </div>
  </div>;
}
