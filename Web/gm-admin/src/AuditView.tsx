import { useState } from 'react';
import { Segmented, Select, Table, Typography } from 'antd';
import type { AuditPage, AuditRecord } from './api';
import { bjTime } from './format';
import { useGmRead } from './useGmRead';
import { PageHead, Pill, type PillKind } from './ui';

export const actionLabels: Record<string, string> = { ChangeEvent: '管理活动', ChangeJoustParameters: '修改马战玩法参数', SetJoustChampion: '设置本场马战冠军',
  ChangeDailyRefill: '修改每日补给', SaveInitialization: '保存初始化配置', Login: '登录', Logout: '退出', SendOfficialMail: '发送官方邮件',
  SaveMailDraft: '保存邮件草稿', EditMailTemplate: '修改内容模板', GrantCera: '发放点券' };
export const outcomeLabels: Record<string, [string, PillKind]> = { Succeeded: ['成功', 'ok'], Rejected: ['已拒绝', 'attn'], RolledBack: ['已回滚', 'attn'], Unknown: ['结果未确认', 'crit'] };
/** Mail and cera operations record their request ID (32 hex) as the target; show the familiar dashed prefix. */
export const auditTarget = (record: AuditRecord) => /^[0-9a-f]{32}$/i.test(record.target) ? `编号 ${record.target.slice(0, 8)}` : record.target;

export function AuditView({ onExpired, onDenied }: { onExpired: () => void; onDenied: () => void }) {
  const [page, setPage] = useState(1); const [pageSize, setPageSize] = useState(20);
  const [outcome, setOutcome] = useState('all'); const [action, setAction] = useState<string>();
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (outcome !== 'all') params.set('outcome', outcome);
  if (action) params.set('action', action);
  const query = useGmRead<AuditPage>(`audit?${params}`, onExpired, onDenied);
  const data = query.data;
  return <div className="page">
    <PageHead title="操作记录" info="所有管理员的写操作与登录，按时间倒序。" />
    <section className="panel">
      <div className="toolbar">
        <Segmented size="small" value={outcome} onChange={value => { setOutcome(String(value)); setPage(1); }} options={[{ label: '全部', value: 'all' },
          { label: '需关注', value: 'attention' }, { label: '成功', value: 'Succeeded' }, { label: '未确认', value: 'Unknown' }, { label: '已回滚', value: 'RolledBack' }, { label: '已拒绝', value: 'Rejected' }]} />
        <Select size="small" allowClear placeholder="全部操作" aria-label="操作类型" style={{ width: 170 }} value={action} onChange={value => { setAction(value); setPage(1); }}
          options={Object.entries(actionLabels).map(([value, label]) => ({ value, label }))} />
        <span className="faint mono small" style={{ marginInlineStart: 'auto' }}>{data ? `${data.total} 条` : ''}</span>
      </div>
      <Table<AuditRecord> className="ledger" size="small" rowKey="operationId" dataSource={data?.items ?? []} loading={query.busy} scroll={{ x: 980 }}
        locale={{ emptyText: query.error ? '读取失败' : '这一类没有记录' }}
        pagination={{ current: page, pageSize, total: data?.total ?? 0, size: 'small', showSizeChanger: true, showTotal: total => `共 ${total} 条`,
          onChange: (next, size) => { setPage(size === pageSize ? next : 1); setPageSize(size); } }}
        columns={[
          { title: '时间', dataIndex: 'occurredAt', width: 150, render: v => <span className="mono small">{bjTime(v, true)}</span> },
          { title: '操作', dataIndex: 'action', render: v => <span className="strong">{actionLabels[v] ?? v}</span> },
          { title: '对象', key: 'target', render: (_, r) => auditTarget(r) },
          { title: '结果', dataIndex: 'outcome', width: 110, render: v => <Pill kind={outcomeLabels[v]?.[1] ?? 'idle'}>{outcomeLabels[v]?.[0] ?? v}</Pill> },
          { title: '说明', dataIndex: 'reason', render: v => v || <span className="faint">—</span> },
          { title: '管理员', dataIndex: 'administratorName', width: 110, render: v => v ?? <span className="faint">未知身份</span> },
          { title: '操作编号', dataIndex: 'operationId', width: 120, render: v => <Typography.Text copyable={{ text: v }} className="mono faint" style={{ fontSize: 11.5 }}>{String(v).slice(0, 8)}</Typography.Text> },
        ]} />
    </section>
  </div>;
}
