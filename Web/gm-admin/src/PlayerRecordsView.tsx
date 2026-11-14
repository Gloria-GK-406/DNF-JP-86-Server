import { useState } from 'react';
import { Segmented, Table } from 'antd';
import { bjTime } from './format';
import { usePlayer } from './PlayerApp';
import type { PlayerOperationsPage, PlayerReceipt } from './playerApi';
import { useGmRead } from './useGmRead';
import { Pill } from './ui';

const kindNames: Record<string, string> = { cera: '点券', mail: '邮件' };
const ignore = () => {};

export function PlayerRecordsView() {
  const { call } = usePlayer();
  const [kind, setKind] = useState('all'); const [page, setPage] = useState(1);
  const params = new URLSearchParams({ page: String(page), pageSize: '20' });
  if (kind !== 'all') params.set('kind', kind);
  // The shell already turned 401 / portal_closed into a page change; nothing else to route here.
  const query = useGmRead<PlayerOperationsPage>(`operations?${params}`, ignore, ignore, call);
  const data = query.data;
  return <div className="page">
    <section className="panel">
      <div className="panel-head"><Segmented size="small" aria-label="记录类型" value={kind} onChange={value => { setKind(String(value)); setPage(1); }}
        options={[{ label: '全部', value: 'all' }, { label: '点券', value: 'cera' }, { label: '邮件', value: 'mail' }]} /></div>
      <Table<PlayerReceipt> size="small" rowKey="requestId" dataSource={data?.items ?? []} loading={query.busy} scroll={{ x: 520 }}
        locale={{ emptyText: query.error ? '读取失败' : '无记录' }}
        pagination={{ current: page, pageSize: 20, total: data?.total ?? 0, size: 'small', showSizeChanger: false, onChange: setPage }}
        columns={[
          { title: '时间', dataIndex: 'committedAt', width: 140, render: v => <span className="mono small dim" style={{ whiteSpace: 'nowrap' }}>{bjTime(v)}</span> },
          { title: '类型', dataIndex: 'kind', width: 70, render: v => <span className="chip">{kindNames[v] ?? v}</span> },
          { title: '内容', dataIndex: 'summary' },
          { title: '结果', dataIndex: 'result', width: 80, render: v => <Pill kind={v === '成功' ? 'ok' : 'idle'}>{v}</Pill> },
        ]} />
    </section>
  </div>;
}
