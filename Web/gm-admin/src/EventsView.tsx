import { useEffect, useRef, useState } from 'react';
import { App, Button, Card, Form, Input, Modal, Space, Table, Tag, Typography } from 'antd';
import { api, ApiError } from './api';
import { useGmRead } from './useGmRead';
import { NotiHint, useNotiBar } from './NotiBar';

type Reward = { attendanceNo: number; itemId: number; count: number };
export type EventEntry = { eventId: number; type: string; revision: number; seasonId: string; enabled: boolean;
  status: 'active' | 'disabled' | 'scheduled' | 'expired' | 'unavailable'; title: string; shortName: string;
  description: string; startNotice: string; endNotice: string; startsAt: string | null; endsAt: string | null;
  sourcePath: string; available: boolean; unavailableReason: string | null; requiredClears: number; totalWeeks: number;
  weeklyRewards: Reward[]; cumulativeRewards: Reward[]; updatedAt: string | null; updatedBy: string | null };
export type EventList = { entries: EventEntry[] };
type Access = { onExpired: () => void; onDenied: () => void };
type Configuration = Pick<EventEntry, 'title' | 'shortName' | 'description' | 'startNotice' | 'endNotice'> & { startsAt: string; endsAt: string };
type Action = 'configure' | 'enable' | 'disable' | 'new-season';
const statuses = { active: '生效中', disabled: '已停用', scheduled: '未开始', expired: '已结束', unavailable: '规则不可用' };
const successes = { configure: '活动配置已保存，当前期次进度保留。', enable: '活动已启用，按配置时间生效。',
  disable: '活动已停用，当前期次进度保留。', 'new-season': '新期次已建立，当前处于停用状态；启用后玩家从头开始。' };
function displayTime(value: string | null) { return value ? new Date(value).toLocaleString('zh-CN', { timeZone: 'Asia/Shanghai', hour12: false }) : '尚未配置'; }
function inputTime(value: string | null) { return value ? new Date(new Date(value).getTime() + 8 * 3600000).toISOString().slice(0, 19) : ''; }
function rewards(items: Reward[], cumulative = false) {
  return items.length ? items.map(reward => `${cumulative ? '累计' : '第'} ${reward.attendanceNo} 次：物品 ${reward.itemId} × ${reward.count}`).join('；') : '暂无可用奖励';
}

export function EventsView(access: Access) {
  const current = useGmRead<EventList>('events', access.onExpired, access.onDenied);
  const noti = useNotiBar(); const { modal } = App.useApp();
  const [editor, setEditor] = useState<{ entry: EventEntry; action: 'configure' | 'new-season' }>();
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
    setEditor(undefined); reloading.current = true; current.refresh();
  }
  function confirm(entry: EventEntry) {
    modal.confirm({ title: `${entry.enabled ? '停用' : '启用'}「${entry.title}」？`,
      content: entry.enabled ? '停用后玩家不能继续累计或签到领奖；本期进度和已领取记录保留。' : '活动仅在配置的有效期内生效；本期进度和已领取记录保留，不会重复发奖。',
      okText: entry.enabled ? '确认停用' : '确认启用', cancelText: '取消', onOk: () => change(entry, entry.enabled ? 'disable' : 'enable') });
  }
  const locked = busy || uncertain || current.busy || !current.data;
  return <Space orientation="vertical" size="large" style={{ width: '100%' }}>
    <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>累计周签到按账号参与；完成推荐地下城后达到每周目标，可在游戏内签到并通过邮箱领取奖励。启停保留本期进度，新期次从头开始。</Typography.Paragraph>
    {uncertain && <NotiHint noticeKey="events-reload-required" title="请重新读取生效活动后再操作" description="配置已发生冲突或操作结果未确认，重新读取成功后才能继续修改。" />}
    <Card title="已注册活动" extra={<Button disabled={busy || current.busy} onClick={reload}>{uncertain ? '重新读取生效活动' : '刷新活动'}</Button>}>
      <Typography.Paragraph type="secondary">时间均为北京时间（UTC+8）。奖励规则由活动内容提供，可在此配置展示信息与有效期。</Typography.Paragraph>
      <Table<EventEntry> rowKey="eventId" dataSource={current.data?.entries ?? []} loading={current.busy} scroll={{ x: 1300 }}
        expandable={{ expandedRowRender: row => <><Typography.Paragraph>每周奖励：{rewards(row.weeklyRewards)}</Typography.Paragraph><Typography.Paragraph>累计奖励：{rewards(row.cumulativeRewards, true)}</Typography.Paragraph></> }}
        pagination={{ hideOnSinglePage: true }} columns={[
          { title: '活动', width: 185, render: (_, row) => <><Typography.Text strong>{row.title}</Typography.Text><div>{row.shortName}</div><div className="mail-item-id">ID {row.eventId}</div></> },
          { title: '类型 / 期次', width: 210, render: (_, row) => <><div>{row.type === 'weekly-attendance' ? '累计周签到' : row.type}</div><Typography.Text copyable>{row.seasonId}</Typography.Text><div className="mail-item-id">配置版本 {row.revision}</div></> },
          { title: '状态', width: 140, render: (_, row) => <><Tag color={row.status === 'active' ? 'green' : row.status === 'unavailable' ? 'orange' : undefined}>{statuses[row.status]}</Tag>
            {!row.available && <div>{row.unavailableReason ?? '活动规则暂不可用'}</div>}{!row.startsAt && <div className="mail-item-id">配置有效期后才能启用</div>}</> },
          { title: '有效期（北京时间）', width: 200, render: (_, row) => <>{displayTime(row.startsAt)}<br />至 {displayTime(row.endsAt)}</> },
          { title: '规则与奖励', width: 330, render: (_, row) => <><div>每周推荐通关 {row.requiredClears} 次 · 共 {row.totalWeeks} 次签到</div>
            <Typography.Paragraph type="secondary" style={{ marginBottom: 4 }}>来源：{row.sourcePath}</Typography.Paragraph>
            <div>每周奖励：{rewards(row.weeklyRewards.slice(0, 1))}{row.weeklyRewards.length > 1 && ` 等 ${row.weeklyRewards.length} 组（展开查看）`}</div><div>累计奖励：{rewards(row.cumulativeRewards, true)}</div></> },
          { title: '操作', width: 235, render: (_, row) => <Space wrap>
            <Button size="small" disabled={locked} onClick={() => setEditor({ entry: structuredClone(row), action: 'configure' })}>编辑配置</Button>
            <Button size="small" danger={row.enabled} disabled={locked || (!row.enabled && (!row.available || !row.startsAt || !row.endsAt))} onClick={() => confirm(row)}>{row.enabled ? '停用' : '启用'}</Button>
            <Button size="small" disabled={locked} onClick={() => setEditor({ entry: structuredClone(row), action: 'new-season' })}>新建期次</Button>
          </Space> },
        ]} />
    </Card>
    {editor && <EventEditor key={`${editor.action}:${editor.entry.eventId}:${editor.entry.revision}`} {...editor} busy={busy} uncertain={uncertain}
      onClose={() => setEditor(undefined)} onReload={reload} onSave={configuration => change(editor.entry, editor.action, configuration)} />}
  </Space>;
}

function EventEditor({ entry, action, busy, uncertain, onClose, onReload, onSave }: {
  entry: EventEntry; action: 'configure' | 'new-season'; busy: boolean; uncertain: boolean;
  onClose: () => void; onReload: () => void; onSave: (configuration: Configuration) => Promise<void>;
}) {
  const [form] = Form.useForm(); const { modal } = App.useApp(); const noti = useNotiBar();
  const [dirty, setDirty] = useState(false); const confirming = useRef(false);
  function close() {
    if (busy || confirming.current) return;
    if (!dirty) onClose(); else modal.confirm({ title: '放弃尚未保存的修改？', okText: '放弃修改', cancelText: '继续编辑', onOk: onClose });
  }
  async function finish(values: Record<string, string>) {
    if (busy || uncertain || confirming.current) return;
    const startsAt = new Date(entry.startsAt && values.startsAt === inputTime(entry.startsAt) ? entry.startsAt : `${values.startsAt}+08:00`);
    const endsAt = new Date(entry.endsAt && values.endsAt === inputTime(entry.endsAt) ? entry.endsAt : `${values.endsAt}+08:00`);
    const valid = (date: Date) => Number.isFinite(date.getTime()) && date.getTime() >= 0 && date.getTime() / 1000 <= 4294967295;
    if (!valid(startsAt) || !valid(endsAt) || endsAt <= startsAt) { noti.error('请填写有效的起止时间，结束时间须晚于开始时间。'); return; }
    const configuration: Configuration = { title: values.title.trim(), shortName: values.shortName.trim(), description: (values.description ?? '').trim(),
      startNotice: (values.startNotice ?? '').trim(), endNotice: (values.endNotice ?? '').trim(), startsAt: startsAt.toISOString(), endsAt: endsAt.toISOString() };
    if (action === 'configure') await onSave(configuration);
    else {
      confirming.current = true;
      modal.confirm({ title: '确认新建活动期次？', content: '旧期次的进度、领取记录和已发邮件保留。新期次的玩家进度从头开始，创建后默认停用，需要另行启用。',
        okText: '确认新建期次', cancelText: '继续编辑', onOk: () => onSave(configuration), afterClose: () => { confirming.current = false; } });
    }
  }
  const textRules = [{ validator: (_: unknown, value: string | undefined) => value?.includes('\0') ? Promise.reject(new Error('不能包含空字符')) : Promise.resolve() }];
  return <Modal open title={action === 'configure' ? '编辑活动配置' : '新建活动期次'} width={720} onCancel={close} closable={!busy} keyboard={!busy}
    mask={{ closable: false }} destroyOnHidden footer={<Space><Button disabled={busy} onClick={close}>取消</Button>
      {uncertain ? <Button type="primary" disabled={busy} onClick={onReload}>重新读取生效活动</Button> :
        <Button type="primary" loading={busy} onClick={() => form.submit()}>{action === 'configure' ? '保存配置' : '新建期次'}</Button>}</Space>}>
    <Typography.Paragraph type="secondary">{action === 'configure' ? '保存配置保留本期进度；已有进度时不能修改有效期。活动启停请使用列表中的独立操作。' : '请核对新期次的展示信息和有效期；奖励与签到目标沿用活动规则。'}</Typography.Paragraph>
    <Form form={form} layout="vertical" initialValues={{ title: entry.title, shortName: entry.shortName, description: entry.description,
      startNotice: entry.startNotice, endNotice: entry.endNotice, startsAt: inputTime(entry.startsAt), endsAt: inputTime(entry.endsAt) }}
      disabled={busy || uncertain} onValuesChange={() => setDirty(true)} onFinish={finish} onFinishFailed={() => noti.error('请核对活动配置字段。')}>
      <Form.Item name="title" label="活动标题" rules={[{ required: true, whitespace: true, max: 80, message: '请输入 1—80 字的活动标题' }, ...textRules]}><Input maxLength={80} /></Form.Item>
      <Form.Item name="shortName" label="活动短名" rules={[{ required: true, whitespace: true, max: 40, message: '请输入 1—40 字的活动短名' }, ...textRules]}><Input maxLength={40} /></Form.Item>
      <Form.Item name="description" label="活动说明" rules={[{ max: 1000 }, ...textRules]}><Input.TextArea rows={3} maxLength={1000} placeholder="介绍每周签到方式与奖励领取方式" /></Form.Item>
      <Form.Item name="startNotice" label="开始公告" rules={[{ max: 200 }, ...textRules]}><Input.TextArea rows={2} maxLength={200} placeholder="活动开始时展示的公告文案（可留空）" /></Form.Item>
      <Form.Item name="endNotice" label="结束公告" rules={[{ max: 200 }, ...textRules]}><Input.TextArea rows={2} maxLength={200} placeholder="活动结束时展示的公告文案（可留空）" /></Form.Item>
      <Form.Item name="startsAt" label="开始时间（北京时间 UTC+8）" rules={[{ required: true, message: '请配置开始时间' }]}><Input type="datetime-local" step="1" /></Form.Item>
      <Form.Item name="endsAt" label="结束时间（北京时间 UTC+8）" rules={[{ required: true, message: '请配置结束时间' }]}><Input type="datetime-local" step="1" /></Form.Item>
    </Form>
  </Modal>;
}
