import { useEffect, useRef, useState } from 'react';
import { App, Button, Card, Descriptions, Form, Input, Modal, Space, Spin, Table, Tag, Typography } from 'antd';
import { api, ApiError } from '../api';
import { NotiHint, useNotiBar } from '../NotiBar';
import type { ActivitySettingsProps } from './WeeklyAttendanceSettings';

export type JoustSchedule = { dailyStartTime: string; roundsPerDay: number; roundIntervalMinutes: number;
  bettingMinutes: number; lockMinutes: number; stageSeconds: number };
type Knight = { index: number; name: string; attackType: number; win: number[]; loss: number[] };
type Item = { itemId: number; itemName: string | null; archivedExpiresAt: string | null };
export type JoustSettingsData = { eventId: number; revision: number; schedule: JoustSchedule; sourcePath: string;
  available: boolean; unavailableReason: string | null; minimumLevel: number | null; maximumBetting: number | null;
  knights: Knight[]; currencies: Item[]; reward: Item | null; bettingReward: Item | null; updatedAt: string | null; updatedBy: string | null };
type Draft = { [K in keyof JoustSchedule]: string };
const path = 'events/joust/settings';
const fields = [
  { key: 'roundsPerDay', label: '每天期数', min: 1, max: 24 },
  { key: 'roundIntervalMinutes', label: '每期间隔（分钟）', min: 1, max: 1440 },
  { key: 'bettingMinutes', label: '下注时长（分钟）', min: 1, max: 1440 },
  { key: 'lockMinutes', label: '封盘时长（分钟）', min: 0, max: 1440 },
  { key: 'stageSeconds', label: '每个比赛阶段（秒）', min: 1, max: 86400 },
] as const;
function draftOf(schedule: JoustSchedule): Draft {
  return { dailyStartTime: schedule.dailyStartTime, roundsPerDay: String(schedule.roundsPerDay),
    roundIntervalMinutes: String(schedule.roundIntervalMinutes), bettingMinutes: String(schedule.bettingMinutes),
    lockMinutes: String(schedule.lockMinutes), stageSeconds: String(schedule.stageSeconds) };
}
function scheduleOf(draft: Draft): JoustSchedule | undefined {
  if (!/^([01]\d|2[0-3]):[0-5]\d$/.test(draft.dailyStartTime)) return;
  for (const field of fields) {
    const value = Number(draft[field.key]);
    if (!draft[field.key].trim() || !Number.isInteger(value) || value < field.min || value > field.max) return;
  }
  const schedule = { dailyStartTime: draft.dailyStartTime, roundsPerDay: Number(draft.roundsPerDay),
    roundIntervalMinutes: Number(draft.roundIntervalMinutes), bettingMinutes: Number(draft.bettingMinutes),
    lockMinutes: Number(draft.lockMinutes), stageSeconds: Number(draft.stageSeconds) };
  const [hours, minutes] = schedule.dailyStartTime.split(':').map(Number);
  if (schedule.bettingMinutes * 60 + schedule.lockMinutes * 60 + schedule.stageSeconds * 3 >= schedule.roundIntervalMinutes * 60
    || hours * 60 + minutes + schedule.roundsPerDay * schedule.roundIntervalMinutes > 1440) return;
  return schedule;
}
function summary(schedule: JoustSchedule) {
  return `${schedule.dailyStartTime} 开始，每天 ${schedule.roundsPerDay} 期，间隔 ${schedule.roundIntervalMinutes} 分钟；下注 ${schedule.bettingMinutes} 分钟，封盘 ${schedule.lockMinutes} 分钟，3 个比赛阶段各 ${schedule.stageSeconds} 秒。`;
}
function itemLabel(item: Item | null) {
  return item ? <span>{item.itemName ?? '名称未找到'}（<Typography.Text copyable>{item.itemId}</Typography.Text>）</span> : '暂无可用物品';
}

export function JoustSettings({ title, onClose, onExpired, onDenied }: ActivitySettingsProps) {
  const noti = useNotiBar(); const { modal } = App.useApp();
  const [data, setData] = useState<JoustSettingsData>(); const [draft, setDraft] = useState<Draft>();
  const [reading, setReading] = useState(true); const [saving, setSaving] = useState(false);
  const [readError, setReadError] = useState(false); const [uncertain, setUncertain] = useState(false);
  const [reviewRequired, setReviewRequired] = useState(false);
  const alive = useRef(true); const writing = useRef(false); const loading = useRef(false); const blocked = useRef(true);
  const confirming = useRef(false); const access = useRef({ onExpired, onDenied }); access.current = { onExpired, onDenied };
  function failure(failure: unknown) {
    if (failure instanceof ApiError && failure.status === 401) access.current.onExpired();
    else if (failure instanceof ApiError && failure.status === 403) access.current.onDenied();
    else noti.failure(failure);
  }
  async function read() {
    if (writing.current || loading.current || !alive.current) return;
    loading.current = true; blocked.current = true; setReading(true); setReadError(false);
    try {
      const next = await api<JoustSettingsData>(path);
      if (!alive.current) return;
      const dirty = draft && data && JSON.stringify(draft) !== JSON.stringify(draftOf(data.schedule));
      if (!dirty && !reviewRequired) setDraft(draftOf(next.schedule));
      else if (data && JSON.stringify(next.schedule) !== JSON.stringify(data.schedule)) setReviewRequired(true);
      setData(next);
      blocked.current = false; setUncertain(false);
    } catch (error) {
      if (alive.current) { setReadError(true); failure(error); }
    } finally { loading.current = false; if (alive.current) setReading(false); }
  }
  useEffect(() => { alive.current = true; void read(); return () => { alive.current = false; }; }, []);
  async function save(schedule: JoustSchedule) {
    if (!alive.current || writing.current || loading.current || blocked.current || !data?.available) return;
    writing.current = true; setSaving(true);
    try {
      const next = await api<JoustSettingsData>(path, { expectedRevision: data.revision, schedule });
      if (alive.current) { setData(next); setDraft(draftOf(next.schedule)); setReviewRequired(false); noti.success('马战赛程已保存，已生成轮次沿用原赛程。'); }
    } catch (error) {
      if (!alive.current) return;
      if (!(error instanceof ApiError) || error.status >= 500 || error.status === 409 || error.outcome === 'unknown') {
        blocked.current = true; setUncertain(true); setReviewRequired(true);
      }
      failure(error);
    } finally { writing.current = false; if (alive.current) setSaving(false); }
  }
  function submit() {
    if (!draft || writing.current || loading.current || blocked.current || confirming.current || !data?.available) return;
    const schedule = scheduleOf(draft);
    if (!schedule) { noti.error('请填写有效赛程：时间格式为 HH:mm，各阶段总时长必须小于每期间隔，保留结果展示时间，每天赛程不能跨午夜。'); return; }
    if (!reviewRequired) { void save(schedule); return; }
    confirming.current = true;
    modal.confirm({ title: '确认保存核对后的赛程？', content: `将以服务器配置版本 ${data.revision} 保存当前编辑赛程。${summary(schedule)}`,
      okText: '确认保存赛程', cancelText: '继续编辑', onOk: () => save(schedule), afterClose: () => { confirming.current = false; } });
  }
  function close() {
    if (writing.current || confirming.current) return;
    if (draft && data && JSON.stringify(draft) !== JSON.stringify(draftOf(data.schedule))) {
      confirming.current = true;
      modal.confirm({ title: '放弃尚未保存的赛程修改？', okText: '放弃修改', cancelText: '继续编辑', onOk: onClose,
        afterClose: () => { confirming.current = false; } });
    } else onClose();
  }
  const busy = reading || saving;
  return <Modal open title={`设置活动 · ${title}`} width={960} onCancel={close} closable={!saving} keyboard={!saving}
    mask={{ closable: false }} destroyOnHidden styles={{ body: { maxHeight: 'calc(100vh - 240px)', overflowY: 'auto', paddingRight: 8 } }}
    footer={<Space><Button disabled={busy} onClick={() => void read()}>{uncertain || readError ? '重新读取生效赛程' : '重新读取设置'}</Button>
      <Button disabled={saving} onClick={close}>关闭</Button><Button type="primary" loading={saving}
        disabled={busy || readError || uncertain || !data?.available || !draft} onClick={submit}>{reviewRequired ? '核对后保存赛程' : '保存赛程'}</Button></Space>}>
    <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
      <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>赛程使用北京时间（UTC+8）。活动有效期与启停在通用配置中设置；赛程修改只影响尚未生成的轮次。</Typography.Paragraph>
      {reading && <Spin />}
      {readError && <Typography.Text type="secondary">赛程读取失败，请重新读取成功后再保存。编辑内容已保留。</Typography.Text>}
      {uncertain && <NotiHint noticeKey="joust-reload-required" title="请重新读取生效赛程后再保存" description="配置冲突或保存结果未确认。编辑内容已保留，重读成功后请核对生效赛程再保存。" />}
      {data && <>
        <Descriptions size="small" column={1} items={[
          { key: 'source', label: '规则来源', children: <Typography.Text copyable>{data.sourcePath}</Typography.Text> },
          { key: 'revision', label: '赛程配置版本', children: data.revision },
          { key: 'available', label: '规则状态', children: data.available ? '可用' : (data.unavailableReason ?? '马战规则暂不可用') },
        ]} />
        {reviewRequired && <Card size="small" title="当前生效赛程"><Typography.Paragraph style={{ marginBottom: 0 }}>{summary(data.schedule)}</Typography.Paragraph></Card>}
        {draft && <Card size="small" title="每日赛程">
          <Form layout="vertical" disabled={busy || !data.available}>
            <Form.Item label="每天开始时间（北京时间 UTC+8）" htmlFor="joust-dailyStartTime"><Input id="joust-dailyStartTime" type="time" step="60" value={draft.dailyStartTime}
              onChange={event => setDraft({ ...draft, dailyStartTime: event.target.value })} /></Form.Item>
            {fields.map(field => <Form.Item key={field.key} label={field.label} htmlFor={`joust-${field.key}`}><Input id={`joust-${field.key}`}
              type="number" min={field.min} max={field.max} step="1" value={draft[field.key]} onChange={event => setDraft({ ...draft, [field.key]: event.target.value })} /></Form.Item>)}
          </Form>
          <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>比赛固定为四分之一决赛、半决赛、决赛三个阶段；每期间隔扣除下注、封盘和比赛阶段后为赛果展示时间。</Typography.Paragraph>
        </Card>}
        {data.available && <>
          <Card size="small" title={<><Tag color="blue">PVF · 只读</Tag>竞猜规则与奖励</>}>
            <Descriptions size="small" column={1} items={[
              { key: 'level', label: '最低参与等级', children: data.minimumLevel == null ? '暂不可用' : `${data.minimumLevel} 级` },
              { key: 'limit', label: '每角色每轮投注上限', children: data.maximumBetting == null ? '暂不可用' : data.maximumBetting },
              { key: 'currencies', label: '下注材料', children: <Space orientation="vertical">{data.currencies.map(item => <span key={item.itemId}>{itemLabel(item)}</span>)}</Space> },
              { key: 'reward', label: '中奖奖励', children: itemLabel(data.reward) },
              { key: 'participation', label: '参与奖励', children: itemLabel(data.bettingReward) },
            ]} />
          </Card>
          <Card size="small" title="骑士（PVF · 只读）"><Table<Knight> size="small" rowKey="index" pagination={false} dataSource={data.knights}
            columns={[{ title: '骑士名称（ID）', render: (_, knight) => <span>{knight.name}（{knight.index}）</span> }]} /></Card>
        </>}
      </>}
    </Space>
  </Modal>;
}
