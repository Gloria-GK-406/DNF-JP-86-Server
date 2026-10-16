import { useState } from 'react';
import { Button, Table, Tooltip } from 'antd';
import { ArrowRightOutlined, CheckOutlined, EditOutlined, ExclamationCircleFilled, QuestionCircleOutlined, ReloadOutlined, RollbackOutlined, StopOutlined, WalletOutlined } from '@ant-design/icons';
import type { AuditPage, AuditRecord, Overview } from './api';
import { useNav } from './AdminApp';
import { actionLabels, auditTarget, outcomeLabels } from './AuditView';
import { EventTimeline, type EventList } from './EventsView';
import { bjClock, bjTime, nf } from './format';
import { useGrants } from './pending';
import { useGmRead } from './useGmRead';
import { PageHead, Panel, Pill } from './ui';

export function SampleTime({ value }: { value?: string }) {
  return <span className="faint mono small">{value ? `采样 ${bjClock(value)}` : '等待采样'}</span>;
}
const ackKey = (scope: string) => `dfo.gm.ack.${scope}`;
function readAcks(scope: string): string[] { try { const value = JSON.parse(localStorage.getItem(ackKey(scope)) ?? '[]'); return Array.isArray(value) ? value.filter(x => typeof x === 'string') : []; } catch { return []; } }

export function OverviewView({ onExpired, onDenied }: { onExpired: () => void; onDenied: () => void }) {
  const nav = useNav(); const grants = useGrants();
  const query = useGmRead<Overview>('overview', onExpired, onDenied);
  const canAudit = nav.can('ViewAudit'); const canConfigure = nav.can('All');
  const ignore = () => {};
  const attention = useGmRead<AuditPage>(canAudit ? 'audit?page=1&pageSize=20&outcome=attention' : null, onExpired, ignore);
  const recent = useGmRead<AuditPage>(canAudit ? 'audit?page=1&pageSize=7' : null, onExpired, ignore);
  const events = useGmRead<EventList>(canConfigure ? 'events' : null, onExpired, ignore);
  const scope = 'overview';
  const [acks, setAcks] = useState(() => readAcks(scope));
  function ack(id: string) { const next = [...acks, id].slice(-200); setAcks(next); try { localStorage.setItem(ackKey(scope), JSON.stringify(next)); } catch { /* per-browser convenience */ } }
  const pendings = [grants.mail && { kind: 'mail' as const, id: grants.mail.requestId, title: grants.mail.input.title || '无标题邮件' },
    grants.cera && { kind: 'cera' as const, id: grants.cera.requestId, title: `${grants.cera.input.accountIds.length} 个账号` }].filter(x => !!x);
  const pendingIds = new Set(pendings.map(p => p.id.replaceAll('-', '')));
  const flagged = (attention.data?.items ?? []).filter(a => !acks.includes(a.operationId) && !(a.outcome === 'Unknown' && pendingIds.has(a.target)));
  const todo = pendings.length + flagged.length;
  const data = query.data;
  const activeEvents = events.data?.entries.filter(e => e.status === 'active').length;
  const nextEvent = events.data?.entries.filter(e => e.status === 'scheduled' && e.startsAt).sort((a, b) => a.startsAt!.localeCompare(b.startsAt!))[0];
  const max = Math.max(1, ...(data?.channels.map(c => c.onlineCharacters) ?? [1]));
  const link = (a: AuditRecord) => a.action.includes('Joust') || a.action === 'ChangeEvent' ? '/events' : a.action === 'ChangeDailyRefill' ? '/daily-refill'
    : a.action === 'SaveInitialization' ? '/initialization' : ['SendOfficialMail', 'GrantCera', 'EditMailTemplate', 'SaveMailDraft'].includes(a.action) ? '/grants' : '/operations';
  return <div className="page">
    <PageHead title="服务概览" info="在线账号包含停在选角界面的连接；频道人数只计已入场角色。">
      <SampleTime value={data?.sampledAt} /><Button size="small" icon={<ReloadOutlined />} loading={query.busy} onClick={() => { query.refresh(); attention.refresh(); recent.refresh(); events.refresh(); }}>刷新概览</Button>
      {nav.can('GrantCera') && <Button size="small" icon={<WalletOutlined />} disabled={!!grants.cera} onClick={() => nav.composeCera()}>发点券</Button>}
      {nav.can('SendMail') && <Button size="small" type="primary" icon={<EditOutlined />} disabled={!!grants.mail} onClick={() => nav.composeMail()}>写邮件</Button>}
    </PageHead>
    <section className="panel" aria-label="关键数字"><div className="stats">
      <div className="stat"><span className="l">已入场角色</span><span className="v">{data ? nf(data.onlineCharacters) : '—'}</span><span className="s">{data ? `${data.channels.length} 个频道 · 未映射 ${data.unmappedOnlineCharacters}` : ''}</span></div>
      <div className="stat"><span className="l">在线账号</span><span className="v">{data ? nf(data.onlineAccounts) : '—'}</span><span className="s">{data ? `${Math.max(0, data.onlineAccounts - data.onlineCharacters)} 个停在选角` : ''}</span></div>
      <div className="stat"><span className="l">账号总数</span><span className="v">{data ? nf(data.totalAccounts) : '—'}</span><span className="s">{data ? (data.networkEnabled ? '游戏网络已启用' : '游戏网络已关闭') : ''}</span></div>
      <div className="stat"><span className="l">角色总数</span><span className="v">{data ? nf(data.totalCharacters) : '—'}</span><span className="s">{data ? (data.gameReady ? '游戏服务已就绪' : '游戏服务未就绪') : ''}</span></div>
      <div className="stat"><span className="l">生效中活动</span><span className="v">{activeEvents ?? '—'}</span><span className="s">{!canConfigure ? '需要全部权限' : nextEvent ? `${bjTime(nextEvent.startsAt).slice(5, 10)} 开始 ${nextEvent.title}` : '暂无待开始'}</span></div>
      <div className={`stat ${pendings.length ? 'crit' : todo ? 'attn' : 'calm'}`}><span className="l">需要处理</span><span className="v">{todo}</span>
        <span className="s">{pendings.length ? `含 ${pendings.length} 笔未确认发放` : todo ? '拒绝 / 回滚待你看一眼' : '都处理完了'}</span></div>
    </div></section>
    <div className="grid g-2">
      <Panel title="需要处理" count={todo} flush tools={canAudit && <button className="linkbtn" onClick={() => nav.go('/operations')}>全部记录 <ArrowRightOutlined /></button>}>
        {todo === 0 && <div className="empty-line">{canAudit ? '都处理完了，喝口水吧' : '没有待确认的发放'}</div>}
        {pendings.map(p => <div className="todo-item" key={p.id}><span className="ti crit"><ExclamationCircleFilled /></span>
          <div>{p.kind === 'cera' ? '点券发放' : '官方邮件'}结果未确认<small>{p.title} · 先查原编号，别重新发</small></div>
          <Button size="small" danger onClick={() => nav.go('/grants')}>去处理</Button></div>)}
        {flagged.map(a => <div className="todo-item" key={a.operationId}>
          <span className="ti attn">{a.outcome === 'RolledBack' ? <RollbackOutlined /> : a.outcome === 'Unknown' ? <QuestionCircleOutlined /> : <StopOutlined />}</span>
          <div>{actionLabels[a.action] ?? a.action}{outcomeLabels[a.outcome]?.[0] ?? a.outcome}<small>{bjClock(a.occurredAt)} · {auditTarget(a)}{a.reason ? ` · ${a.reason}` : ''}</small></div>
          <span style={{ display: 'flex', gap: 4 }}><Button size="small" onClick={() => nav.go(link(a))}>看看</Button>
            <Tooltip title="知道了，从这里移走"><Button size="small" type="text" aria-label="知道了" icon={<CheckOutlined />} onClick={() => ack(a.operationId)} /></Tooltip></span></div>)}
      </Panel>
      <Panel title="频道在线" count={data ? `${data.onlineCharacters} 人 · 未映射 ${data.unmappedOnlineCharacters}` : undefined}>
        {!data ? <div className="empty-line">{query.busy ? '正在读取…' : '暂未读取到概览'}</div> : data.channels.length === 0 ? <div className="empty-line">当前没有已入场角色</div> :
          <div className="bars">{data.channels.map(c => <div className="bar-row" key={c.channelId}><span className="mono" title={`频道 ID ${c.channelId}`}>{c.name}</span>
            <div className="bar-track" role="img" aria-label={`${c.name} ${c.onlineCharacters} 人`}><div className="bar-fill" style={{ width: `${c.onlineCharacters / max * 100}%` }} /></div>
            <span className="n">{c.onlineCharacters}</span></div>)}</div>}
      </Panel>
    </div>
    {(canConfigure || canAudit) && <div className="grid g-2">
      {canConfigure && <Panel title="活动排期" tools={<button className="linkbtn" onClick={() => nav.go('/events')}>管理 <ArrowRightOutlined /></button>}>
        {events.data ? <EventTimeline events={events.data.entries} /> : <div className="empty-line">{events.busy ? '正在读取…' : '暂未读取到活动'}</div>}</Panel>}
      {canAudit && <Panel title="最近操作" flush tools={<button className="linkbtn" onClick={() => nav.go('/operations')}>全部 <ArrowRightOutlined /></button>}>
        <Table<AuditRecord> className="ledger" size="small" rowKey="operationId" pagination={false} loading={recent.busy} dataSource={recent.data?.items ?? []} scroll={{ x: 520 }}
          locale={{ emptyText: '还没有操作记录' }} columns={[
            { title: '时间', dataIndex: 'occurredAt', width: 64, render: v => <span className="mono small">{bjClock(v)}</span> },
            { title: '操作', key: 'a', render: (_, a) => <span><span className="strong">{actionLabels[a.action] ?? a.action}</span> <span className="faint">· {auditTarget(a)}</span></span> },
            { title: '结果', dataIndex: 'outcome', width: 100, render: v => <Pill kind={outcomeLabels[v]?.[1] ?? 'idle'}>{outcomeLabels[v]?.[0] ?? v}</Pill> }]} />
      </Panel>}
    </div>}
  </div>;
}
