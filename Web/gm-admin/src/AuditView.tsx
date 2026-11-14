import { useState } from 'react';
import { Segmented, Select, Table, Typography } from 'antd';
import type { AuditPage, AuditRecord } from './api';
import { bjTime } from './format';
import { useGmRead } from './useGmRead';
import { PageHead, Pill, type PillKind } from './ui';

export const actionLabels: Record<string, string> = { ChangeEvent: '管理活动', ChangeJoustParameters: '修改马战玩法参数', SetJoustChampion: '设置本场马战冠军',
  ChangeDailyRefill: '修改每日补给', SaveInitialization: '保存初始化配置', Login: '登录', Logout: '退出', SendOfficialMail: '发送官方邮件',
  SaveMailDraft: '保存邮件草稿', EditMailTemplate: '修改内容模板', GrantCera: '发放点券', PlayerLogin: '玩家登录', PlayerLogout: '玩家退出',
  PlayerGrantCera: '玩家加点券', PlayerSendMail: '玩家发邮件', ChangePlayerPortal: '修改玩家后台设置', KickPlayerSessions: '踢玩家下线' };
export const outcomeLabels: Record<string, [string, PillKind]> = { Succeeded: ['成功', 'ok'], Rejected: ['已拒绝', 'attn'], RolledBack: ['已回滚', 'attn'], Unknown: ['结果未确认', 'crit'] };
/** Mail and cera operations record their request ID (32 hex) as the target; show the familiar dashed prefix. */
export const auditTarget = (record: AuditRecord) => /^[0-9a-f]{32}$/i.test(record.target) ? `编号 ${record.target.slice(0, 8)}` : record.target;

export function AuditView({ onExpired, onDenied }: { onExpired: () => void; onDenied: () => void }) {
  const [page, setPage] = useState(1); const [pageSize, setPageSize] = useState(20);
  const [outcome, setOutcome] = useState('all'); const [action, setAction] = useState<string>(); const [actor, setActor] = useState<string>();
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (outcome !== 'all') params.set('outcome', outcome);
  if (action) params.set('action', action);
  if (actor) params.set('actor', actor);
  const query = useGmRead<AuditPage>(`audit?${params}`, onExpired, onDenied);
  const data = query.data;
  return <div className="page">
    <PageHead title="操作记录" info="管理员与玩家自助的写操作与登录，按时间倒序。" />
    <section className="panel">
      <div className="toolbar">
        <Segmented size="small" value={outcome} onChange={value => { setOutcome(String(value)); setPage(1); }} options={[{ label: '全部', value: 'all' },
          { label: '需关注', value: 'attention' }, { label: '成功', value: 'Succeeded' }, { label: '未确认', value: 'Unknown' }, { label: '已回滚', value: 'RolledBack' }, { label: '已拒绝', value: 'Rejected' }]} />
        <Select size="small" allowClear placeholder="全部操作" aria-label="操作类型" style={{ width: 170 }} value={action} onChange={value => { setAction(value); setPage(1); }}
          options={Object.entries(actionLabels).map(([value, label]) => ({ value, label }))} />
        <Select size="small" allowClear placeholder="全部操作者" aria-label="操作者" style={{ width: 120 }} value={actor} onChange={value => { setActor(value); setPage(1); }}
          options={[{ value: 'gm', label: '管理员' }, { value: 'player', label: '玩家自助' }]} />
        <span className="faint mono small" style={{ marginInlineStart: 'auto' }}>{data ? `${data.total} 条` : ''}</span>
      </div>
      <Table<AuditRecord> className="ledger" size="small" rowKey="operationId" dataSource={data?.items ?? []} loading={query.busy} scroll={{ x: 980 }}
        locale={{ emptyText: query.error ? '读取失败' : '这一类没有记录' }}
        pagination={{ current: page, pageSize, total: data?.total ?? 0, size: 'small', showSizeChanger: true, showTotal: total => `共 ${total} 条`,
          onChange: (next, size) => { setPage(size === pageSize ? next : 1); setPageSize(size); } }}
        columns={[
          { title: '时间', dataIndex: 'occurredAt', width: 172, render: v => <span className="mono small" style={{ whiteSpace: 'nowrap' }}>{bjTime(v, true)}</span> },
          { title: '操作', dataIndex: 'action', render: v => <span className="strong">{actionLabels[v] ?? v}</span> },
          { title: '对象', key: 'target', render: (_, r) => auditTarget(r) },
          { title: '结果', dataIndex: 'outcome', width: 110, render: v => <Pill kind={outcomeLabels[v]?.[1] ?? 'idle'}>{outcomeLabels[v]?.[0] ?? v}</Pill> },
          { title: '说明', dataIndex: 'reason', render: v => v || <span className="faint">—</span> },
          { title: '操作者', key: 'actor', width: 150, render: (_, r) => <AuditActor record={r} /> },
          { title: '操作编号', dataIndex: 'operationId', width: 120, render: v => <Typography.Text copyable={{ text: v }} className="mono faint" style={{ fontSize: 11.5 }}>{String(v).slice(0, 8)}</Typography.Text> },
        ]} />
    </section>
  </div>;
}
/** Player rows name the account (login name and ID); administrator rows keep the administrator's name. */
export function AuditActor({ record }: { record: AuditRecord }) {
  if (record.actorKind === 'player') return <span><span className="chip">玩家</span> {record.administratorName ?? <span className="faint">未知身份</span>}
    {record.accountId != null && <span className="id" style={{ marginInlineStart: 6 }}>{record.accountId}</span>}</span>;
  return <>{record.administratorName ?? <span className="faint">未知身份</span>}</>;
}
