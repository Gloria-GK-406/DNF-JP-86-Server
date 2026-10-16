import { useEffect, useRef, useState } from 'react';
import { Button, Popconfirm, Table, Typography } from 'antd';
import { Panel } from '../ui';
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
  const forced = data?.knights.find(k => k.index === data.forcedChampionIndex);
  const pick = selection ? data?.knights.find(k => k.index === selection.knightIndex) : undefined;
  return <div className="grid">
    <div style={{ display: 'flex', gap: 10, alignItems: 'center', flexWrap: 'wrap' }}><Button size="small" loading={reading} disabled={saving} onClick={() => void read(true)}>重新读取当前场次</Button>
      <span className="faint small">每 5 秒刷新；投注与封盘阶段均可指定冠军，第一轮战斗开始后不可修改。</span></div>
    {readError && <span className="faint small">当前场次读取失败，请重新读取后再操作。</span>}
    {uncertain && <NotiHint noticeKey="joust-round-reload" title="请重新读取当前场次后再操作" description="场次可能已变化，或保存结果未确认。核对当前内定冠军后再提交。" />}
    {stale && <span className="err small">场次版本已更新，请重新读取后选择骑士。</span>}
    {data && (data.roundId ? <>
      <section className="panel"><div className="stats" style={{ gridTemplateColumns: 'repeat(4, minmax(0, 1fr))' }}>
        <div className="stat"><span className="l">当前阶段</span><span className="v" style={{ fontSize: 17 }}>{phaseLabels[data.phase] ?? data.phase}</span>
          <span className="s">第 {data.roundNo} 期 · {data.active ? '活动生效中' : '活动未生效 · 只读'}</span></div>
        <div className="stat"><span className="l">场次开始时间</span><span className="v mono" style={{ fontSize: 17 }}>{time(data.startsAt).slice(-8, -3) || '—'}</span><span className="s">{time(data.startsAt)}</span></div>
        <div className={`stat ${data.canSetChampion ? 'attn' : ''}`}><span className="l">冠军设置截止</span><span className="v mono" style={{ fontSize: 17 }}>{time(data.championDeadline).slice(-8, -3) || '—'}</span>
          <span className="s">{data.canSetChampion ? '现在可以指定' : '已不可修改'}</span></div>
        <div className="stat"><span className="l">本场总投注</span><span className="v" style={{ fontSize: 17 }}>{data.knights.reduce((sum, k) => sum + k.bettingAmount, 0).toLocaleString()}</span>
          <span className="s">当前内定冠军：{data.forcedChampionIndex == null ? '未指定' : forced?.name ?? data.forcedChampionIndex}</span></div>
      </div></section>
      <Panel title="骑士" count={`场次 ${data.roundId} · 版本 ${data.revision}`} flush foot={<><span className="grow">仅影响本场晋级结果；投注中赔率继续变化，封盘后保持不变；下一场重新抽签。</span>
        <Button size="small" disabled={!allowed || !!stale || data.forcedChampionIndex == null} onClick={() => void save(true)}>取消内定</Button>
        <Popconfirm title={`把 ${pick?.name ?? ''} 设为本场冠军？`} okText="就它了" cancelText="算了" onConfirm={() => void save()}
          disabled={!allowed || !selection || !!stale || chosen === data.forcedChampionIndex}>
          <Button size="small" type="primary" loading={saving} disabled={!allowed || !selection || !!stale || chosen === data.forcedChampionIndex}>设为本场冠军</Button></Popconfirm></>}>
        <Table<CurrentKnight> className="ledger" size="small" rowKey="index" pagination={false} dataSource={data.knights} scroll={{ x: 900 }}
          rowSelection={{ type: 'radio', selectedRowKeys: chosen == null ? [] : [chosen], getCheckboxProps: k => ({ disabled: !allowed, 'aria-label': `选择 ${k.name} 作为本场冠军` }),
            onChange: keys => { if (allowed && data.roundId) setSelection({ roundId: data.roundId, expectedRevision: data.revision, knightIndex: Number(keys[0]) }); } }}
          columns={[
            { title: '骑士名称（ID）', render: (_, k) => <span><span className="strong">{k.name}（{k.index}）</span>{data.forcedChampionIndex === k.index && <span className="chip accent" style={{ marginInlineStart: 6 }}>内定冠军</span>}</span> },
            { title: '状态 / 战斗力', render: (_, k) => k.condition == null ? '—' : <span>{['强势', '常态', '弱势'][k.condition] ?? k.condition} <span className="mono faint small">{k.combatMultiplier?.toFixed(2)}×</span></span> },
            { title: '攻击类型', dataIndex: 'attackType' },
            { title: '预测夺冠率', render: (_, k) => k.championshipProbability == null ? '—' : <span style={{ display: 'inline-flex', gap: 8, alignItems: 'center' }}>
              <span className="meter-track" style={{ width: 56 }}><span className="meter-fill" style={{ width: `${Math.min(100, k.championshipProbability * 200)}%` }} /></span>
              <span className="mono">{`${(k.championshipProbability * 100).toFixed(1)}%`}</span></span> },
            { title: '当前赔率', align: 'end', render: (_, k) => <span className="mono">{`${k.odds.toFixed(1)}×`}</span> },
            { title: '总投注量', align: 'end', render: (_, k) => <span className="mono">{k.bettingAmount.toLocaleString()}</span> },
            { title: '历史胜 / 败', align: 'end', render: (_, k) => <span className="mono">{`${k.statistics.wins} / ${k.statistics.losses}`}</span> },
            { title: '历史胜率', align: 'end', render: (_, k) => <span className="mono">{`${k.statistics.winRate.toFixed(1)}%`}</span> },
          ]} />
      </Panel>
      {data.updatedAt && <span className="faint small">最近操作：{data.updatedBy ?? '—'} · {time(data.updatedAt)}</span>}
    </> : <div className="empty-line">当前没有已生成的场次</div>)}
  </div>;
}
