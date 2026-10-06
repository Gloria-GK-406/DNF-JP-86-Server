import { useEffect, useRef, useState } from 'react';
import { App, Button, Card, Descriptions, Form, Input, Modal, Space, Spin, Table, Tabs, Tag, Typography } from 'antd';
import { api, ApiError } from '../api';
import { NotiHint, useNotiBar } from '../NotiBar';
import type { ActivitySettingsProps } from './WeeklyAttendanceSettings';
import { JoustCurrentRound } from './JoustCurrentRound';

export type JoustSchedule = { dailyStartTime: string; roundsPerDay: number; roundIntervalMinutes: number;
  bettingMinutes: number; lockMinutes: number; stageSeconds: number };
export type JoustParameters = { strongMultiplier: number; normalMultiplier: number; weakMultiplier: number;
  counterMultiplier: number; historyPrior: number; forecastWeight: number; bettingScale: number };
type Knight = { index: number; name: string; attackType: number; win: number[]; loss: number[] };
type Item = { itemId: number; itemName: string | null; archivedExpiresAt: string | null };
export type JoustSettingsData = { eventId: number; revision: number; schedule: JoustSchedule; parameters: JoustParameters; sourcePath: string;
  available: boolean; unavailableReason: string | null; minimumLevel: number | null; maximumBetting: number | null;
  knights: Knight[]; currencies: Item[]; reward: Item | null; bettingReward: Item | null; updatedAt: string | null; updatedBy: string | null };
type Draft = { [K in keyof JoustParameters]: string };
const path = 'events/joust/settings';
const fields = [
  { key: 'strongMultiplier', label: '强势战斗力倍率', min: 0.1, max: 3, step: 0.05 },
  { key: 'normalMultiplier', label: '常态战斗力倍率', min: 0.1, max: 3, step: 0.05 },
  { key: 'weakMultiplier', label: '弱势战斗力倍率', min: 0.1, max: 3, step: 0.05 },
  { key: 'counterMultiplier', label: '攻击克制倍率', min: 1, max: 3, step: 0.05 },
  { key: 'historyPrior', label: '历史战绩先验场数', min: 1, max: 1000, step: 1 },
  { key: 'forecastWeight', label: '本期预测修正权重', min: 0, max: 1, step: 0.05 },
  { key: 'bettingScale', label: '投注衰减基数', min: 1, max: 1000000, step: 100 },
] as const;
function draftOf(parameters: JoustParameters): Draft {
  return Object.fromEntries(fields.map(f => [f.key, String(parameters[f.key])])) as Draft;
}
function parametersOf(draft: Draft): JoustParameters | undefined {
  for (const f of fields) {
    const v = Number(draft[f.key]);
    if (!draft[f.key].trim() || !Number.isFinite(v) || v < f.min || v > f.max || (f.step >= 1 && !Number.isInteger(v))) return;
  }
  const p = Object.fromEntries(fields.map(f => [f.key, Number(draft[f.key])])) as JoustParameters;
  return p.weakMultiplier <= p.normalMultiplier && p.normalMultiplier <= p.strongMultiplier ? p : undefined;
}
function itemLabel(item: Item | null) {
  return item ? <span>{item.itemName ?? '名称未找到'}（<Typography.Text copyable>{item.itemId}</Typography.Text>）</span> : '暂无可用物品';
}
function clockText(minutes: number) { return `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`; }

export function JoustSettings({ title, onClose, onExpired, onDenied }: ActivitySettingsProps) {
  const noti = useNotiBar(); const { modal } = App.useApp();
  const [data, setData] = useState<JoustSettingsData>(); const [draft, setDraft] = useState<Draft>();
  const [tab, setTab] = useState('parameters'); const [reading, setReading] = useState(true); const [saving, setSaving] = useState(false);
  const [roundWriting, setRoundWriting] = useState(false); const [readError, setReadError] = useState(false);
  const [uncertain, setUncertain] = useState(false); const [reviewRequired, setReviewRequired] = useState(false);
  const alive = useRef(true); const writing = useRef(false); const loading = useRef(false); const blocked = useRef(true);
  const confirming = useRef(false); const access = useRef({ onExpired, onDenied }); access.current = { onExpired, onDenied };
  function failure(error: unknown) {
    if (error instanceof ApiError && error.status === 401) access.current.onExpired();
    else if (error instanceof ApiError && error.status === 403) access.current.onDenied();
    else noti.failure(error);
  }
  async function read() {
    if (writing.current || loading.current || !alive.current) return;
    loading.current = true; blocked.current = true; setReading(true); setReadError(false);
    try {
      const next = await api<JoustSettingsData>(path); if (!alive.current) return;
      const dirty = draft && data && JSON.stringify(draft) !== JSON.stringify(draftOf(data.parameters));
      if (!dirty && !reviewRequired) setDraft(draftOf(next.parameters));
      else if (data && JSON.stringify(next.parameters) !== JSON.stringify(data.parameters)) setReviewRequired(true);
      setData(next); blocked.current = false; setUncertain(false);
    } catch (error) { if (alive.current) { setReadError(true); failure(error); } }
    finally { loading.current = false; if (alive.current) setReading(false); }
  }
  useEffect(() => { alive.current = true; void read(); return () => { alive.current = false; }; }, []);
  async function save(parameters: JoustParameters) {
    if (!alive.current || writing.current || loading.current || blocked.current || !data?.available) return;
    writing.current = true; setSaving(true);
    try {
      const next = await api<JoustSettingsData>(path, { expectedRevision: data.revision, parameters });
      if (alive.current) { setData(next); setDraft(draftOf(next.parameters)); setReviewRequired(false); noti.success('玩法参数已保存，将从新场次生效。'); }
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
    const parameters = parametersOf(draft);
    if (!parameters) { noti.error('请填写有效参数，并满足弱势倍率 ≤ 常态倍率 ≤ 强势倍率。'); return; }
    if (!reviewRequired) { void save(parameters); return; }
    confirming.current = true;
    modal.confirm({ title: '确认保存核对后的玩法参数？', content: `将以配置版本 ${data.revision} 保存，当前场次沿用原参数。`,
      okText: '确认保存参数', cancelText: '继续编辑', onOk: () => save(parameters), afterClose: () => { confirming.current = false; } });
  }
  function close() {
    if (writing.current || roundWriting || confirming.current) return;
    if (draft && data && JSON.stringify(draft) !== JSON.stringify(draftOf(data.parameters))) {
      confirming.current = true;
      modal.confirm({ title: '放弃尚未保存的玩法参数？', okText: '放弃修改', cancelText: '继续编辑', onOk: onClose,
        afterClose: () => { confirming.current = false; } });
    } else onClose();
  }
  const busy = reading || saving || roundWriting;
  const schedule = data?.schedule;
  const sessions = schedule ? Array.from({ length: schedule.roundsPerDay }, (_, index) => {
    const [h, m] = schedule.dailyStartTime.split(':').map(Number); const start = h * 60 + m + index * schedule.roundIntervalMinutes;
    return { index, start: clockText(start), lock: clockText(start + schedule.bettingMinutes), fight: clockText(start + schedule.bettingMinutes + schedule.lockMinutes) };
  }) : [];
  return <Modal open title={`设置活动 · ${title}`} width={1120} onCancel={close} closable={!saving && !roundWriting} keyboard={!saving && !roundWriting}
    mask={{ closable: false }} destroyOnHidden styles={{ body: { maxHeight: 'calc(100vh - 240px)', overflowY: 'auto', paddingRight: 8 } }}
    footer={<Space>{tab !== 'current' && <Button disabled={busy} onClick={() => void read()}>{uncertain || readError ? '重新读取生效参数' : '重新读取设置'}</Button>}
      <Button disabled={saving || roundWriting} onClick={close}>关闭</Button>{tab === 'parameters' && <Button type="primary" loading={saving}
        disabled={busy || readError || uncertain || !data?.available || !draft} onClick={submit}>{reviewRequired ? '核对后保存参数' : '保存玩法参数'}</Button>}</Space>}>
    {reading && !data && <Spin />}
    {readError && <Typography.Paragraph type="secondary">设置读取失败，请重新读取。编辑内容已保留。</Typography.Paragraph>}
    {uncertain && <NotiHint noticeKey="joust-reload-required" title="请重新读取生效参数后再保存" description="配置冲突或保存结果未确认。编辑内容已保留，重读后请核对。" />}
    <Tabs activeKey={tab} onChange={key => { if (!saving && !roundWriting) setTab(key); }} items={[
      { key: 'parameters', label: '玩法参数', children: data && <Space orientation="vertical" style={{ width: '100%' }} size="middle">
        <Typography.Paragraph type="secondary">参数只影响新场次。当前场次的状态、预测概率和封盘赔率保持原快照。</Typography.Paragraph>
        <Descriptions size="small" column={2} items={[{ key: 'revision', label: '配置版本', children: data.revision },
          { key: 'available', label: '规则状态', children: data.available ? '可用' : data.unavailableReason }]} />
        {reviewRequired && <Card size="small" title="当前生效参数"><Descriptions size="small" column={2} items={fields.map(f => ({ key: f.key, label: f.label, children: data.parameters[f.key] }))} /></Card>}
        {draft && <Card size="small" title="战斗与赔率参数"><Form layout="vertical" disabled={busy || !data.available}
          style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', columnGap: 24 }}>
          {fields.map(f => <Form.Item key={f.key} label={f.label} htmlFor={`joust-${f.key}`}><Input id={`joust-${f.key}`} type="number"
            min={f.min} max={f.max} step={f.step} value={draft[f.key]} onChange={event => setDraft({ ...draft, [f.key]: event.target.value })} /></Form.Item>)}
        </Form><Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>倍率 1 为无加成；克制反向使用倒数。预测概率 12.5% 时不修正历史基准。投注衰减基数越大，赔率下降越慢。</Typography.Paragraph></Card>}
        <Card size="small" title={<><Tag color="blue">PVF · 只读</Tag>竞猜规则与奖励</>}>
          <Descriptions size="small" column={2} items={[
            { key: 'source', label: '规则来源', children: <Typography.Text copyable>{data.sourcePath}</Typography.Text> },
            { key: 'level', label: '最低参与等级', children: data.minimumLevel == null ? '暂不可用' : `${data.minimumLevel} 级` },
            { key: 'limit', label: '每角色每场投注上限', children: data.maximumBetting },
            { key: 'currencies', label: '下注材料', children: <Space orientation="vertical">{data.currencies.map(item => <span key={item.itemId}>{itemLabel(item)}</span>)}</Space> },
            { key: 'reward', label: '中奖奖励', children: itemLabel(data.reward) }, { key: 'participation', label: '参与奖励', children: itemLabel(data.bettingReward) },
          ]} />
        </Card>
        <Card size="small" title="骑士资料（PVF · 只读）"><Table<Knight> size="small" rowKey="index" pagination={false} dataSource={data.knights}
          columns={[{ title: '骑士名称（ID）', render: (_, k) => `${k.name}（${k.index}）` }, { title: '攻击类型', dataIndex: 'attackType' }]} /></Card>
      </Space> },
      { key: 'schedule', label: '每日赛程', children: schedule && <Space orientation="vertical" style={{ width: '100%' }} size="middle">
        <Typography.Paragraph type="secondary">客户端素材采用固定时间，每日赛程只读。活动有效期与启停仍在通用配置中管理。</Typography.Paragraph>
        <Descriptions bordered size="small" column={3} items={[
          { key: 'start', label: '每天开始时间', children: `${schedule.dailyStartTime}（北京时间）` },
          { key: 'count', label: '每天场次', children: schedule.roundsPerDay }, { key: 'interval', label: '场次间隔', children: `${schedule.roundIntervalMinutes} 分钟` },
          { key: 'bet', label: '投注时长', children: `${schedule.bettingMinutes} 分钟` }, { key: 'lock', label: '封盘时长', children: `${schedule.lockMinutes} 分钟` },
          { key: 'stage', label: '每轮比赛时长', children: `${schedule.stageSeconds} 秒` },
        ]} />
        <Table size="small" rowKey="index" pagination={false} dataSource={sessions} columns={[
          { title: '每日场次', render: (_, row) => `第 ${row.index + 1} 场` }, { title: '开始投注', dataIndex: 'start' },
          { title: '投注封盘', dataIndex: 'lock' }, { title: '战斗开始', dataIndex: 'fight' },
        ]} />
      </Space> },
      { key: 'current', label: '当前场次', children: tab === 'current' && <JoustCurrentRound onExpired={onExpired} onDenied={onDenied} onWriting={setRoundWriting} /> },
    ]} />
  </Modal>;
}
