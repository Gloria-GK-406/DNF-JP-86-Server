import { useEffect, useRef, useState } from 'react';
import { App, Button, Drawer, Dropdown, Input, Popconfirm, Table, Typography } from 'antd';
import { EllipsisOutlined, ReloadOutlined } from '@ant-design/icons';
import { api, ApiError } from './api';
import { activityView } from './events/activityViews';
import { bjTime } from './format';
import { useNotiBar } from './NotiBar';
import { Field, LockBanner, PageHead, Panel, Pill, type PillKind } from './ui';
import { useGmRead } from './useGmRead';

export type EventEntry = { eventId: number; type: string; revision: number; seasonId: string; enabled: boolean;
  status: 'active' | 'disabled' | 'scheduled' | 'expired' | 'unavailable'; title: string; shortName: string;
  description: string; startNotice: string; endNotice: string; startsAt: string | null; endsAt: string | null;
  available: boolean; unavailableReason: string | null; updatedAt: string | null; updatedBy: string | null };
export type EventList = { entries: EventEntry[] };
type Access = { onExpired: () => void; onDenied: () => void };
type Configuration = Pick<EventEntry, 'title' | 'shortName' | 'description' | 'startNotice' | 'endNotice'> & { startsAt: string; endsAt: string };
type Action = 'configure' | 'enable' | 'disable' | 'new-season';
const statuses: Record<EventEntry['status'], [string, PillKind]> = { active: ['生效中', 'ok'], disabled: ['已停用', 'attn'], scheduled: ['未开始', 'idle'], expired: ['已结束', 'idle'], unavailable: ['规则不可用', 'crit'] };
const successes = { configure: '活动配置已保存，当前期次进度保留。', enable: '活动已启用，按配置时间生效。',
  disable: '活动已停用，当前期次进度保留。', 'new-season': '新期次已建立，当前处于停用状态；启用后玩家从头开始。' };
function inputTime(value: string | null) { return value ? new Date(new Date(value).getTime() + 8 * 3600000).toISOString().slice(0, 19) : ''; }

/** Schedule lanes over a fixed window around today (Beijing time), 50 days wide. */
export function EventTimeline({ events }: { events: EventEntry[] }) {
  const day = 86400000; const today = Date.now();
  const from = today - 25 * day; const to = today + 25 * day;
  const pos = (t: number) => Math.min(100, Math.max(0, (t - from) / (to - from) * 100));
  // Ticks sit at ±10 and ±20 days so the 今天 marker at the centre never collides with one.
  const ticks = [-20, -10, 10, 20].map(offset => today + offset * day);
  const rows = events.filter(e => e.startsAt && e.endsAt);
  if (rows.length === 0) return <div className="empty-line">还没有配置了有效期的活动</div>;
  return <div className="timeline" aria-label="活动排期">
    <div className="tl-row"><span className="faint" style={{ fontSize: 11.5 }}>北京时间</span><div className="tl-scale">{ticks.map(t => <span key={t} style={{ left: `${pos(t)}%` }}>{bjTime(t).slice(5, 10)}</span>)}
      <span style={{ left: `${pos(today)}%`, color: 'var(--crit)' }}>今天</span></div></div>
    {rows.map(e => { const start = new Date(e.startsAt!).getTime(); const end = new Date(e.endsAt!).getTime();
      const kind = e.status === 'active' ? 'active' : e.status === 'scheduled' ? 'scheduled' : 'expired';
      return <div className="tl-row" key={e.eventId}><span className="name" title={`${e.title} · ${bjTime(e.startsAt)} → ${bjTime(e.endsAt)}`}>{e.title} <span className="faint">{e.shortName}</span></span>
        <div className="tl-lane"><div className="tl-today" style={{ left: `${pos(today)}%` }} />
          {end >= from && start <= to && <div className={`tl-bar ${kind}`} style={{ left: `${pos(start)}%`, width: `${Math.max(1, pos(end) - pos(start))}%` }} />}</div></div>; })}
    <div className="legend"><span><i style={{ background: 'var(--accent)' }} />生效中</span><span><i style={{ border: '1.5px dashed var(--accent)' }} />未开始</span>
      <span><i style={{ background: 'var(--ink-3)', opacity: .4 }} />已结束 / 停用</span></div>
  </div>;
}

export function EventsView(access: Access) {
  const current = useGmRead<EventList>('events', access.onExpired, access.onDenied);
  const noti = useNotiBar();
  const [editor, setEditor] = useState<{ entry: EventEntry; action: 'configure' | 'new-season' }>();
  const [settings, setSettings] = useState<Pick<EventEntry, 'type' | 'title'>>();
  const [busy, setBusy] = useState(false); const [uncertain, setUncertain] = useState(false);
  const saving = useRef(false); const blocked = useRef(false); const reloading = useRef(false); const alive = useRef(true);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  useEffect(() => {
    if (reloading.current && current.data && !current.busy) { reloading.current = false; blocked.current = false; setUncertain(false); }
  }, [current.data, current.busy]);
  async function change(entry: EventEntry, action: Action, configuration?: Configuration) {
    if (!alive.current || saving.current || blocked.current) return;
    saving.current = true; setBusy(true);
    try {
      await api<EventList>('events', { eventId: entry.eventId, expectedRevision: entry.revision, action, ...configuration });
      if (alive.current) { setEditor(undefined); current.refresh(); noti.success(successes[action]); }
    } catch (failure) {
      if (!alive.current) return;
      if (failure instanceof ApiError && failure.status === 401) access.onExpired();
      else if (failure instanceof ApiError && failure.status === 403) access.onDenied();
      else {
        if (!(failure instanceof ApiError) || failure.status >= 500 || failure.status === 409 || failure.outcome === 'unknown') { blocked.current = true; setUncertain(true); }
        noti.failure(failure);
      }
    } finally { saving.current = false; if (alive.current) setBusy(false); }
  }
  function reload() {
    if (saving.current || current.busy) return;
    setEditor(undefined); setSettings(undefined); reloading.current = true; current.refresh();
  }
  const toggleText = (entry: EventEntry) => entry.type === 'joust'
    ? (entry.enabled ? '停用后禁止新下注；已有投注的轮次仍会继续结算并发放奖励。' : '活动仅在配置的有效期内允许下注；已有投注和奖励记录保留，不会重复发奖。')
    : entry.enabled ? '停用后玩家不能继续参与活动或领取活动奖励；本期进度和已领取记录保留。' : '活动仅在配置的有效期内生效；本期进度和已领取记录保留，不会重复发奖。';
  const locked = busy || uncertain || current.busy || !current.data;
  const Settings = settings && activityView(settings.type)?.Settings;
  const entries = current.data?.entries ?? [];
  return <div className="page">
    <PageHead title="活动管理" info="时间均为北京时间（UTC+8）。启停保留本期进度；需要重新开始时，在“更多”中新建期次。各活动的玩法规则在“设置”里查看。">
      <Button size="small" icon={<ReloadOutlined />} loading={current.busy} disabled={busy} onClick={reload}>{uncertain ? '重新读取生效活动' : '刷新活动'}</Button>
    </PageHead>
    {uncertain && <LockBanner onReload={reload} busy={current.busy} what="活动" label="重新读取生效活动" />}
    <Panel title="排期">{current.data ? <EventTimeline events={entries} /> : <div className="empty-line">{current.busy ? '正在读取…' : '暂未读取到活动'}</div>}</Panel>
    <Panel title="已注册活动" count={entries.length} flush>
      <Table<EventEntry> className="ledger" size="small" rowKey="eventId" dataSource={entries} loading={current.busy} pagination={false} scroll={{ x: 980 }}
        locale={{ emptyText: current.error ? '读取失败' : '没有已注册的活动' }} columns={[
          { title: '活动', key: 't', render: (_, e) => <span><span className="strong">{e.title}</span> <span className="faint small">{e.shortName}</span> <span className="id">#{e.eventId}</span></span> },
          { title: '类型 · 期次', key: 'k', render: (_, e) => <span>{activityView(e.type)?.label ?? e.type} · <Typography.Text copyable={{ text: e.seasonId }} className="mono small">{e.seasonId.length > 14 ? `${e.seasonId.slice(0, 14)}…` : e.seasonId}</Typography.Text>
            <span className="faint small"> v{e.revision}</span></span> },
          { title: '状态', key: 's', width: 120, render: (_, e) => <span><Pill kind={statuses[e.status][1]}>{statuses[e.status][0]}</Pill>
            {!e.available && <div className="faint small">{e.unavailableReason ?? '活动规则暂不可用'}</div>}</span> },
          { title: '有效期（北京时间）', key: 'p', render: (_, e) => e.startsAt ? <span className="mono small">{bjTime(e.startsAt)} → {bjTime(e.endsAt)}</span> : <span className="faint small">配置有效期后才能启用</span> },
          { title: '', key: 'op', align: 'end', width: 270, fixed: 'right', render: (_, e) => <span style={{ display: 'inline-flex', gap: 2, flexWrap: 'wrap', justifyContent: 'flex-end' }}>
            {activityView(e.type) && <Button size="small" onClick={() => setSettings({ type: e.type, title: e.title })}>设置活动</Button>}
            <Button size="small" type="text" disabled={locked} onClick={() => setEditor({ entry: structuredClone(e), action: 'configure' })}>通用配置</Button>
            <Popconfirm title={`${e.enabled ? '停用' : '启用'}「${e.title}」？`} description={toggleText(e)} okText={e.enabled ? '确认停用' : '确认启用'} cancelText="取消"
              onConfirm={() => change(e, e.enabled ? 'disable' : 'enable')} disabled={locked || (!e.enabled && (!e.available || !e.startsAt || !e.endsAt))}>
              <Button size="small" type="text" danger={e.enabled} disabled={locked || (!e.enabled && (!e.available || !e.startsAt || !e.endsAt))}>{e.enabled ? '停用' : '启用'}</Button></Popconfirm>
            <Dropdown trigger={['click']} disabled={locked} menu={{ items: [{ key: 'new-season', label: '新建期次' }], onClick: () => setEditor({ entry: structuredClone(e), action: 'new-season' }) }}>
              <Button size="small" type="text" aria-label="更多" icon={<EllipsisOutlined />} /></Dropdown>
          </span> },
        ]} />
    </Panel>
    {editor && <EventEditor key={`${editor.action}:${editor.entry.eventId}:${editor.entry.revision}`} {...editor} busy={busy} uncertain={uncertain}
      onClose={() => setEditor(undefined)} onReload={reload} onSave={configuration => change(editor.entry, editor.action, configuration)} />}
    {settings && Settings && <Settings title={settings.title} onClose={() => setSettings(undefined)} {...access} />}
  </div>;
}

function EventEditor({ entry, action, busy, uncertain, onClose, onReload, onSave }: {
  entry: EventEntry; action: 'configure' | 'new-season'; busy: boolean; uncertain: boolean;
  onClose: () => void; onReload: () => void; onSave: (configuration: Configuration) => Promise<void>;
}) {
  const { modal } = App.useApp(); const noti = useNotiBar();
  const [form, setForm] = useState(() => ({ title: entry.title, shortName: entry.shortName, description: entry.description, startNotice: entry.startNotice,
    endNotice: entry.endNotice, startsAt: inputTime(entry.startsAt), endsAt: inputTime(entry.endsAt) }));
  const [dirty, setDirty] = useState(false); const [touched, setTouched] = useState(false); const confirming = useRef(false);
  const set = (patch: Partial<typeof form>) => { setForm(old => ({ ...old, ...patch })); setDirty(true); };
  function close() {
    if (busy || confirming.current) return;
    if (!dirty) onClose(); else modal.confirm({ title: '放弃尚未保存的修改？', okText: '放弃修改', cancelText: '继续编辑', onOk: onClose });
  }
  const nul = (value: string) => value.includes('\0');
  const errors = { title: !form.title.trim() || form.title.length > 80 ? '请输入 1—80 字的活动标题' : nul(form.title) ? '不能包含空字符' : '',
    shortName: !form.shortName.trim() || form.shortName.length > 40 ? '请输入 1—40 字的活动短名' : nul(form.shortName) ? '不能包含空字符' : '',
    startsAt: !form.startsAt ? '请配置开始时间' : '', endsAt: !form.endsAt ? '请配置结束时间' : '',
    text: [form.description, form.startNotice, form.endNotice].some(nul) ? '不能包含空字符' : '' };
  async function submit() {
    setTouched(true);
    if (busy || uncertain || confirming.current) return;
    if (Object.values(errors).some(Boolean)) { noti.error('请核对活动配置字段。'); return; }
    // Unchanged times keep the server's seconds and milliseconds so a progress-locked period is not altered by rounding.
    const startsAt = new Date(entry.startsAt && form.startsAt === inputTime(entry.startsAt) ? entry.startsAt : `${form.startsAt}+08:00`);
    const endsAt = new Date(entry.endsAt && form.endsAt === inputTime(entry.endsAt) ? entry.endsAt : `${form.endsAt}+08:00`);
    const valid = (date: Date) => Number.isFinite(date.getTime()) && date.getTime() >= 0 && date.getTime() / 1000 <= 4294967295;
    if (!valid(startsAt) || !valid(endsAt) || endsAt <= startsAt) { noti.error('请填写有效的起止时间，结束时间须晚于开始时间。'); return; }
    const configuration: Configuration = { title: form.title.trim(), shortName: form.shortName.trim(), description: form.description.trim(),
      startNotice: form.startNotice.trim(), endNotice: form.endNotice.trim(), startsAt: startsAt.toISOString(), endsAt: endsAt.toISOString() };
    if (action === 'configure') { await onSave(configuration); return; }
    confirming.current = true;
    modal.confirm({ title: '确认新建活动期次？', content: '旧期次的进度、领取记录和已发邮件保留。新期次的玩家进度从头开始，创建后默认停用，需要另行启用。',
      okText: '确认新建期次', cancelText: '继续编辑', onOk: () => onSave(configuration), afterClose: () => { confirming.current = false; } });
  }
  const disabled = busy || uncertain;
  return <Drawer open size={620} onClose={close} mask={{ closable: false }} closable={!busy} keyboard={!busy} title={action === 'configure' ? '活动通用配置' : '新建活动期次'}
    footer={<div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}><Button disabled={busy} onClick={close}>取消</Button>
      {uncertain ? <Button type="primary" disabled={busy} onClick={onReload}>重新读取生效活动</Button>
        : <Button type="primary" loading={busy} onClick={() => void submit()}>{action === 'configure' ? '保存配置' : '新建期次'}</Button>}</div>}>
    <div className="form">
      <span className="faint small">{action === 'configure' ? entry.type === 'joust' ? '保存配置保留已有轮次和投注；有效期控制新参与，已有投注仍会结算。活动启停请使用列表中的独立操作。'
        : '保存配置保留本期进度；已有进度时不能修改有效期。活动启停请使用列表中的独立操作。' : '请核对新期次的展示信息和有效期；玩法规则沿用该活动的规则。'}</span>
      <div className="fgrid">
        <Field label="活动标题" htmlFor="event-title" err={touched ? errors.title || undefined : undefined} hint="1—80 字"><Input id="event-title" maxLength={80} disabled={disabled} value={form.title} onChange={x => set({ title: x.target.value })} /></Field>
        <Field label="活动短名" htmlFor="event-short" err={touched ? errors.shortName || undefined : undefined} hint="1—40 字"><Input id="event-short" maxLength={40} disabled={disabled} value={form.shortName} onChange={x => set({ shortName: x.target.value })} /></Field>
      </div>
      <Field label="活动说明" htmlFor="event-description" err={touched ? errors.text || undefined : undefined}><Input.TextArea id="event-description" rows={3} maxLength={1000} showCount disabled={disabled}
        placeholder="面向玩家的活动介绍（可留空）" value={form.description} onChange={x => set({ description: x.target.value })} /></Field>
      <div className="fgrid">
        <Field label="开始公告" htmlFor="event-start-notice"><Input.TextArea id="event-start-notice" rows={2} maxLength={200} disabled={disabled} placeholder="活动开始时展示的公告文案（可留空）"
          value={form.startNotice} onChange={x => set({ startNotice: x.target.value })} /></Field>
        <Field label="结束公告" htmlFor="event-end-notice"><Input.TextArea id="event-end-notice" rows={2} maxLength={200} disabled={disabled} placeholder="活动结束时展示的公告文案（可留空）"
          value={form.endNotice} onChange={x => set({ endNotice: x.target.value })} /></Field>
      </div>
      <div className="fgrid">
        <Field label="开始时间（北京时间 UTC+8）" htmlFor="event-starts" err={touched ? errors.startsAt || undefined : undefined}><Input id="event-starts" type="datetime-local" step="1" disabled={disabled}
          value={form.startsAt} onChange={x => set({ startsAt: x.target.value })} /></Field>
        <Field label="结束时间（北京时间 UTC+8）" htmlFor="event-ends" err={touched ? errors.endsAt || undefined : undefined}><Input id="event-ends" type="datetime-local" step="1" disabled={disabled}
          value={form.endsAt} onChange={x => set({ endsAt: x.target.value })} /></Field>
      </div>
    </div>
  </Drawer>;
}
