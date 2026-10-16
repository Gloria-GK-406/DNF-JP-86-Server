import { useState } from 'react';
import { Button, Table, Tooltip } from 'antd';
import { ArrowRightOutlined, CheckOutlined, DownOutlined, EditOutlined, ExclamationCircleFilled, QuestionCircleOutlined, ReloadOutlined, RollbackOutlined, StopOutlined, UpOutlined, WalletOutlined } from '@ant-design/icons';
import type { AuditPage, AuditRecord, Overview } from './api';
import { useNav } from './AdminApp';
import { actionLabels, auditTarget, outcomeLabels } from './AuditView';
import { EventTimeline, type EventList } from './EventsView';
import { bjClock, bjTime, nf } from './format';
import { useGrants } from './pending';
import { useGmRead } from './useGmRead';
import { Banner, MoreToggle, PageHead, Panel, Pill, useExpanded } from './ui';

export function SampleTime({ value }: { value?: string }) {
  return <span className="faint mono small">{value ? `采样 ${bjClock(value)}` : '等待采样'}</span>;
}
/* The home page aims to show every kind of fact at a glance: each block shows its first five rows and expands on demand. */
const LIMIT = 5;
const ackKey = 'dfo.gm.ack.overview';
function readAcks(): string[] { try { const value = JSON.parse(localStorage.getItem(ackKey) ?? '[]'); return Array.isArray(value) ? value.filter(x => typeof x === 'string') : []; } catch { return []; } }
type Group = { key: string; action: string; outcome: string; records: AuditRecord[] };
/** Same action + same outcome collapse into one row; read marks stay in this browser and never change the audit log. */
function groupRecords(records: AuditRecord[]): Group[] {
  const groups = new Map<string, Group>();
  for (const record of records) {
    const key = `${record.action}|${record.outcome}`;
    const group = groups.get(key) ?? { key, action: record.action, outcome: record.outcome, records: [] };
    group.records.push(record); groups.set(key, group);
  }
  return [...groups.values()];
}
const linkOf = (action: string) => action.includes('Joust') || action === 'ChangeEvent' ? '/events' : action === 'ChangeDailyRefill' ? '/daily-refill'
  : action === 'SaveInitialization' ? '/initialization' : ['SendOfficialMail', 'GrantCera', 'EditMailTemplate', 'SaveMailDraft'].includes(action) ? '/grants' : '/operations';

export function OverviewView({ onExpired, onDenied }: { onExpired: () => void; onDenied: () => void }) {
  const nav = useNav(); const grants = useGrants();
  const query = useGmRead<Overview>('overview', onExpired, onDenied);
  const canAudit = nav.can('ViewAudit'); const canConfigure = nav.can('All');
  const ignore = () => {};
  const attention = useGmRead<AuditPage>(canAudit ? 'audit?page=1&pageSize=100&outcome=attention' : null, onExpired, ignore);
  const recent = useGmRead<AuditPage>(canAudit ? 'audit?page=1&pageSize=20' : null, onExpired, ignore);
  const events = useGmRead<EventList>(canConfigure ? 'events' : null, onExpired, ignore);
  const [acks, setAcks] = useState(readAcks);
  const [opened, setOpened] = useState<string | null>(null);
  const [channelsOpen, toggleChannels] = useExpanded('overview.channels');
  const [attentionOpen, toggleAttention] = useExpanded('overview.attention');
  const [recentOpen, toggleRecent] = useExpanded('overview.recent');
  function ack(ids: string[]) { const next = [...new Set([...acks, ...ids])].slice(-500); setAcks(next); try { localStorage.setItem(ackKey, JSON.stringify(next)); } catch { /* per-browser convenience */ } }
  const pendings = [grants.mail && { kind: 'mail' as const, id: grants.mail.requestId, title: grants.mail.input.title || '无标题邮件' },
    grants.cera && { kind: 'cera' as const, id: grants.cera.requestId, title: `${grants.cera.input.accountIds.length} 个账号` }].filter(x => !!x);
  const pendingIds = new Set(pendings.map(p => p.id.replaceAll('-', '')));
  const flagged = (attention.data?.items ?? []).filter(a => !acks.includes(a.operationId) && !(a.outcome === 'Unknown' && pendingIds.has(a.target)));
  const groups = groupRecords(flagged);
  const shownGroups = attentionOpen ? groups : groups.slice(0, LIMIT);
  const data = query.data;
  const activeEvents = events.data?.entries.filter(e => e.status === 'active').length;
  const nextEvent = events.data?.entries.filter(e => e.status === 'scheduled' && e.startsAt).sort((a, b) => a.startsAt!.localeCompare(b.startsAt!))[0];
  const channels = [...(data?.channels ?? [])].sort((a, b) => b.onlineCharacters - a.onlineCharacters || a.channelId - b.channelId);
  const shownChannels = channelsOpen ? channels : channels.slice(0, LIMIT);
  const hiddenPeople = channels.slice(LIMIT).reduce((sum, c) => sum + c.onlineCharacters, 0);
  const max = Math.max(1, ...channels.map(c => c.onlineCharacters));
  const recentRows = recent.data?.items ?? [];
  return <div className="page">
    <PageHead title="服务概览" info="在线账号包含停在选角界面的连接；频道人数只计已入场角色。">
      <SampleTime value={data?.sampledAt} /><Button size="small" icon={<ReloadOutlined />} loading={query.busy} onClick={() => { query.refresh(); attention.refresh(); recent.refresh(); events.refresh(); }}>刷新概览</Button>
      {nav.can('GrantCera') && <Button size="small" icon={<WalletOutlined />} disabled={!!grants.cera} onClick={() => nav.composeCera()}>发点券</Button>}
      {nav.can('SendMail') && <Button size="small" type="primary" icon={<EditOutlined />} disabled={!!grants.mail} onClick={() => nav.composeMail()}>写邮件</Button>}
    </PageHead>
    {pendings.map(p => <Banner key={p.id} kind="crit" icon={<ExclamationCircleFilled />} title={`${p.kind === 'cera' ? '点券发放' : '官方邮件'}结果未确认：${p.title}`}
      actions={<Button size="small" danger onClick={() => nav.go('/grants', { tab: p.kind })}>去处理</Button>}>先用原编号查询或按原内容重试，别重新发。</Banner>)}
    <section className="panel" aria-label="关键数字"><div className="stats">
      <div className="stat"><span className="l">已入场角色</span><span className="v">{data ? nf(data.onlineCharacters) : '—'}</span><span className="s">{data ? `${data.channels.length} 个频道 · 未映射 ${data.unmappedOnlineCharacters}` : ''}</span></div>
      <div className="stat"><span className="l">在线账号</span><span className="v">{data ? nf(data.onlineAccounts) : '—'}</span><span className="s">{data ? `${Math.max(0, data.onlineAccounts - data.onlineCharacters)} 个停在选角` : ''}</span></div>
      <div className="stat"><span className="l">账号总数</span><span className="v">{data ? nf(data.totalAccounts) : '—'}</span><span className="s">{data ? (data.networkEnabled ? '游戏网络已启用' : '游戏网络已关闭') : ''}</span></div>
      <div className="stat"><span className="l">角色总数</span><span className="v">{data ? nf(data.totalCharacters) : '—'}</span><span className="s">{data ? (data.gameReady ? '游戏服务已就绪' : '游戏服务未就绪') : ''}</span></div>
      <div className="stat"><span className="l">生效中活动</span><span className="v">{activeEvents ?? '—'}</span><span className="s">{!canConfigure ? '需要全部权限' : nextEvent ? `${bjTime(nextEvent.startsAt).slice(5, 10)} 开始 ${nextEvent.title}` : '暂无待开始'}</span></div>
      <div className={`stat ${pendings.length ? 'crit' : groups.length ? 'attn' : 'calm'}`}><span className="l">需要处理</span><span className="v">{pendings.length + groups.length}</span>
        <span className="s">{pendings.length ? `含 ${pendings.length} 笔未确认发放` : groups.length ? `${flagged.length} 条拒绝 / 回滚待你看一眼` : '都处理完了'}</span></div>
    </div></section>
    <div className="grid g-2">
      <Panel title="频道在线" count={data ? `${data.onlineCharacters} 人 · 未映射 ${data.unmappedOnlineCharacters}` : undefined}>
        {!data ? <div className="empty-line">{query.busy ? '正在读取…' : '暂未读取到概览'}</div> : channels.length === 0 ? <div className="empty-line">当前没有已入场角色</div> : <>
          <div className="bars">{shownChannels.map(c => <div className="bar-row" key={c.channelId}><span className="mono" title={`频道 ID ${c.channelId}`}>{c.name}</span>
            <div className="bar-track" role="img" aria-label={`${c.name} ${c.onlineCharacters} 人`}><div className="bar-fill" style={{ width: `${c.onlineCharacters / max * 100}%` }} /></div>
            <span className="n">{c.onlineCharacters}</span></div>)}</div>
          <MoreToggle hidden={channels.length - LIMIT} expanded={channelsOpen} onToggle={toggleChannels} summary={`还有 ${channels.length - LIMIT} 个频道 · 共 ${hiddenPeople} 人`} /></>}
      </Panel>
      {canConfigure && <Panel title="活动排期" tools={<button className="linkbtn" onClick={() => nav.go('/events')}>管理 <ArrowRightOutlined /></button>}>
        {events.data ? <EventTimeline events={events.data.entries} limit={LIMIT} expandKey="overview.timeline" /> : <div className="empty-line">{events.busy ? '正在读取…' : '暂未读取到活动'}</div>}</Panel>}
      {canAudit && <Panel title="需要处理" count={flagged.length ? `${groups.length} 类 · ${flagged.length} 条` : 0} flush
        tools={<>{flagged.length > 0 && <button className="linkbtn" onClick={() => ack(flagged.map(a => a.operationId))}><CheckOutlined /> 全部已读</button>}
          <button className="linkbtn" onClick={() => nav.go('/operations')}>全部记录 <ArrowRightOutlined /></button></>}>
        {groups.length === 0 && <div className="empty-line">{attention.busy ? '正在读取…' : '都处理完了，喝口水吧'}</div>}
        {shownGroups.map(group => { const latest = group.records[0]; const open = opened === group.key;
          return <div key={group.key} aria-label={`${actionLabels[group.action] ?? group.action}${outcomeLabels[group.outcome]?.[0] ?? group.outcome}`}>
            <div className="todo-item">
              <span className="ti attn">{group.outcome === 'RolledBack' ? <RollbackOutlined /> : group.outcome === 'Unknown' ? <QuestionCircleOutlined /> : <StopOutlined />}</span>
              <div>{actionLabels[group.action] ?? group.action}{outcomeLabels[group.outcome]?.[0] ?? group.outcome}{group.records.length > 1 && <span className="count">×{group.records.length}</span>}
                <small>{group.records.length > 1 ? '最近 ' : ''}{bjClock(latest.occurredAt)} · {auditTarget(latest)}{latest.reason ? ` · ${latest.reason}` : ''}</small></div>
              <span style={{ display: 'flex', gap: 4 }}>
                {group.records.length > 1 && <Tooltip title={open ? '收起明细' : '看每一条'}><Button size="small" type="text" aria-label={open ? '收起明细' : '展开明细'} aria-expanded={open}
                  icon={open ? <UpOutlined /> : <DownOutlined />} onClick={() => setOpened(open ? null : group.key)} /></Tooltip>}
                <Button size="small" onClick={() => nav.go(linkOf(group.action))}>看看</Button>
                <Tooltip title={group.records.length > 1 ? '这一类全部标为已读' : '知道了，从这里移走'}><Button size="small" type="text" aria-label="知道了" icon={<CheckOutlined />}
                  onClick={() => ack(group.records.map(a => a.operationId))} /></Tooltip></span>
            </div>
            {open && <div className="todo-detail">{group.records.map(a => <span key={a.operationId}>
              <span className="mono">{bjTime(a.occurredAt, true)}</span> · {auditTarget(a)}{a.reason ? ` · ${a.reason}` : ''}{a.administratorName ? ` · ${a.administratorName}` : ''}</span>)}</div>}
          </div>; })}
        <MoreToggle hidden={groups.length - LIMIT} expanded={attentionOpen} onToggle={toggleAttention} summary={`其余 ${groups.length - LIMIT} 类`} />
      </Panel>}
      {canAudit && <Panel title="最近操作" flush tools={<button className="linkbtn" onClick={() => nav.go('/operations')}>全部 <ArrowRightOutlined /></button>}>
        <Table<AuditRecord> className="ledger" size="small" rowKey="operationId" pagination={false} loading={recent.busy} dataSource={recentOpen ? recentRows : recentRows.slice(0, LIMIT)} scroll={{ x: 520 }}
          locale={{ emptyText: '还没有操作记录' }} columns={[
            { title: '时间', dataIndex: 'occurredAt', width: 64, render: v => <span className="mono small">{bjClock(v)}</span> },
            { title: '操作', key: 'a', render: (_, a) => <span><span className="strong">{actionLabels[a.action] ?? a.action}</span> <span className="faint">· {auditTarget(a)}</span></span> },
            { title: '结果', dataIndex: 'outcome', width: 100, render: v => <Pill kind={outcomeLabels[v]?.[1] ?? 'idle'}>{outcomeLabels[v]?.[0] ?? v}</Pill> }]} />
        <MoreToggle hidden={recentRows.length - LIMIT} expanded={recentOpen} onToggle={toggleRecent} summary={`最近 ${recentRows.length} 条`} />
      </Panel>}
    </div>
  </div>;
}
