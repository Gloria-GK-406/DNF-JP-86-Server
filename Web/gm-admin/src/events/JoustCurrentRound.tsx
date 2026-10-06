import { useEffect, useRef, useState } from 'react';
import { Button, Descriptions, Empty, Space, Table, Tag, Typography } from 'antd';
import { api, ApiError } from '../api';
import { NotiHint, useNotiBar } from '../NotiBar';

type CurrentKnight = { index: number; name: string; attackType: number; condition: number | null; combatMultiplier: number | null;
  championshipProbability: number | null; odds: number; bettingAmount: number;
  statistics: { wins: number; losses: number; championships: number; winRate: number } };
export type JoustCurrentRoundData = { roundId: string | null; roundNo: number; revision: number; active: boolean; phase: string;
  serverTime: string; startsAt: string | null; endsAt: string | null; nextTransitionAt: string | null; championDeadline: string | null;
  canSetChampion: boolean; forcedChampionIndex: number | null; updatedAt: string | null; updatedBy: string | null; knights: CurrentKnight[] };
type Selection = { roundId: string; expectedRevision: number; knightIndex: number | null };
const path = 'events/joust/current-round';
const phaseLabels: Record<string, string> = { Inactive: '未开始', Betting: '投注中', Locked: '已封盘 · 等待开战',
  QuarterFinal: '四分之一决赛', SemiFinal: '半决赛', Final: '决赛', Result: '结果展示' };
function time(value: string | null) { return value ? new Date(value).toLocaleString('zh-CN', { timeZone: 'Asia/Shanghai', hour12: false }) : '—'; }

export function JoustCurrentRound({ onExpired, onDenied, onWriting }: { onExpired: () => void; onDenied: () => void; onWriting: (value: boolean) => void }) {
  const noti = useNotiBar(); const [data, setData] = useState<JoustCurrentRoundData>();
  const [selection, setSelection] = useState<Selection>(); const [reading, setReading] = useState(true); const [saving, setSaving] = useState(false);
  const [readError, setReadError] = useState(false); const [uncertain, setUncertain] = useState(false);
  const alive = useRef(true); const writing = useRef(false); const loading = useRef(false); const blocked = useRef(true);
  const snapshot = useRef<JoustCurrentRoundData | undefined>(undefined); const access = useRef({ onExpired, onDenied }); access.current = { onExpired, onDenied };
  function failure(error: unknown) {
    if (error instanceof ApiError && error.status === 401) access.current.onExpired();
    else if (error instanceof ApiError && error.status === 403) access.current.onDenied();
    else noti.failure(error);
  }
  async function read(reset = false) {
    if (writing.current || loading.current || !alive.current) return;
    loading.current = true; setReading(true);
    try {
      const next = await api<JoustCurrentRoundData>(path); if (!alive.current) return;
      const first = !snapshot.current;
      if (reset || snapshot.current?.roundId !== next.roundId) setSelection(undefined);
      snapshot.current = next; setData(next); setReadError(false);
      if (reset || !blocked.current || first) { blocked.current = false; setUncertain(false); }
    } catch (error) { if (alive.current) { blocked.current = true; setReadError(true); failure(error); } }
    finally { loading.current = false; if (alive.current) setReading(false); }
  }
  useEffect(() => {
    alive.current = true; void read(true); const timer = window.setInterval(() => void read(), 5000);
    return () => { alive.current = false; window.clearInterval(timer); };
  }, []);
  const stale = selection && (selection.roundId !== data?.roundId || selection.expectedRevision !== data?.revision);
  const allowed = data?.canSetChampion && !blocked.current && !readError && !uncertain && !saving && !reading;
  const chosen = selection ? selection.knightIndex : data?.forcedChampionIndex;
  async function save(cancel = false) {
    const current = snapshot.current;
    if (!alive.current || writing.current || loading.current || blocked.current || !current?.canSetChampion || !current.roundId) return;
    const change = cancel ? { roundId: current.roundId, expectedRevision: current.revision, knightIndex: null } : selection;
    if (!change || change.roundId !== current.roundId || change.expectedRevision !== current.revision) return;
    writing.current = true; setSaving(true); onWriting(true);
    try {
      const next = await api<JoustCurrentRoundData>(`${path}/champion`, change);
      if (alive.current) { snapshot.current = next; setData(next); setSelection(undefined); noti.success(cancel ? '已取消本场内定冠军，恢复原抽签结果。' : '本场内定冠军已保存。'); }
    } catch (error) {
      if (alive.current) { blocked.current = true; setUncertain(true); failure(error); }
    } finally { writing.current = false; if (alive.current) setSaving(false); onWriting(false); }
  }
  return <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
    <Space wrap><Button loading={reading} disabled={saving} onClick={() => void read(true)}>重新读取当前场次</Button>
      <Typography.Text type="secondary">每 5 秒刷新；仅封盘后、第一轮战斗开始前可指定冠军。</Typography.Text></Space>
    {readError && <Typography.Text type="secondary">当前场次读取失败，请重新读取后再操作。</Typography.Text>}
    {uncertain && <NotiHint noticeKey="joust-round-reload" title="请重新读取当前场次后再操作" description="场次可能已变化，或保存结果未确认。核对当前内定冠军后再提交。" />}
    {stale && <Typography.Text type="secondary">场次版本已更新，请重新读取后选择骑士。</Typography.Text>}
    {data && (data.roundId ? <>
      <Descriptions bordered size="small" column={3} items={[
        { key: 'active', label: '活动状态', children: data.active ? '生效中' : '未生效 · 只读' },
        { key: 'round', label: '当前期号', children: `第 ${data.roundNo} 期` }, { key: 'phase', label: '当前阶段', children: <Tag color={data.canSetChampion ? 'gold' : 'blue'}>{phaseLabels[data.phase] ?? data.phase}{!data.active && ' · 活动未生效'}</Tag> },
        { key: 'start', label: '场次开始时间', children: time(data.startsAt) }, { key: 'deadline', label: '冠军设置截止', children: time(data.championDeadline) },
        { key: 'pool', label: '本场总投注', children: data.knights.reduce((sum, k) => sum + k.bettingAmount, 0).toLocaleString() },
        { key: 'forced', label: '当前内定冠军', children: data.forcedChampionIndex == null ? '未指定' : data.knights.find(k => k.index === data.forcedChampionIndex)?.name ?? data.forcedChampionIndex },
      ]} />
      <Table<CurrentKnight> size="small" rowKey="index" pagination={false} dataSource={data.knights} scroll={{ x: 1000 }}
        rowSelection={{ type: 'radio', selectedRowKeys: chosen == null ? [] : [chosen], getCheckboxProps: k => ({ disabled: !allowed, 'aria-label': `选择 ${k.name} 作为本场冠军` }),
          onChange: keys => { if (allowed && data.roundId) setSelection({ roundId: data.roundId, expectedRevision: data.revision, knightIndex: Number(keys[0]) }); } }}
        columns={[
          { title: '骑士名称（ID）', render: (_, k) => <span>{k.name}（{k.index}）{data.forcedChampionIndex === k.index && <Tag color="gold">内定冠军</Tag>}</span> },
          { title: '状态 / 战斗力', render: (_, k) => k.condition == null ? '—' : `${['强势', '常态', '弱势'][k.condition] ?? k.condition} / ${k.combatMultiplier?.toFixed(2)}×` },
          { title: '攻击类型', dataIndex: 'attackType' }, { title: '预测夺冠率', render: (_, k) => k.championshipProbability == null ? '—' : `${(k.championshipProbability * 100).toFixed(1)}%` },
          { title: '当前赔率', render: (_, k) => `${k.odds.toFixed(1)}×` }, { title: '总投注量', render: (_, k) => k.bettingAmount.toLocaleString() },
          { title: '历史胜 / 败', render: (_, k) => `${k.statistics.wins} / ${k.statistics.losses}` }, { title: '历史胜率', render: (_, k) => `${k.statistics.winRate.toFixed(1)}%` },
        ]} />
      <Space wrap><Button type="primary" loading={saving} disabled={!allowed || !selection || !!stale || chosen === data.forcedChampionIndex}
        onClick={() => void save()}>设为本场冠军</Button><Button disabled={!allowed || !!stale || data.forcedChampionIndex == null} onClick={() => void save(true)}>取消内定</Button>
        <Typography.Text type="secondary">仅影响本场晋级结果，冻结赔率保持不变；下一场重新抽签。</Typography.Text></Space>
      {data.updatedAt && <Typography.Text type="secondary">最近操作：{data.updatedBy ?? '—'} · {time(data.updatedAt)}</Typography.Text>}
    </> : <Empty description="当前没有已生成的场次" />)}
  </Space>;
}
