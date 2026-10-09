import { useState } from 'react';
import { Button, Input, InputNumber, Select, Space, Table, Typography } from 'antd';
import { useGmRead } from './useGmRead';
import { useNotiBar } from './NotiBar';
import { equipmentTypes, itemCategories, itemRarities, type ItemCatalogEntry, type ItemCatalogPage } from './items';

export type ItemAccessCallbacks = { onExpired: () => void; onDenied: () => void };
export type ItemPickerPurpose = 'mail' | 'initialization' | 'daily-refill';
type Filters = { q: string; id: string; category?: string; equipmentType?: string; minimumLevel?: number; maximumLevel?: number; rarity?: number };

export function ItemCatalogSearch({ purpose, selectedIds, onSelect, onExpired, onDenied, multiple = false, configuration }: ItemAccessCallbacks & {
  purpose: ItemPickerPurpose; selectedIds: number[]; onSelect: (item: ItemCatalogEntry) => void; multiple?: boolean; configuration: React.ReactNode;
}) {
  const [filters, setFilters] = useState<Filters>({ q: '', id: '' });
  const [search, setSearch] = useState<Filters>({ q: '', id: '' });
  const [page, setPage] = useState(1);
  const noti = useNotiBar();
  const params = new URLSearchParams({ page: String(page), pageSize: '15' });
  if (search.q.trim()) params.set('q', search.q.trim());
  if (search.id) params.set('itemId', search.id);
  for (const key of ['category', 'equipmentType', 'minimumLevel', 'maximumLevel', 'rarity'] as const)
    if (search[key] !== undefined) params.set(key, String(search[key]));
  const results = useGmRead<ItemCatalogPage>(`${purpose}/items?${params}`, onExpired, onDenied);
  function apply(next: Filters) {
    if (next.id && (!/^\d+$/.test(next.id) || +next.id <= 0 || +next.id > 2147483647)) { noti.error('物品 ID 必须为正整数。'); return; }
    if (next.minimumLevel !== undefined && next.maximumLevel !== undefined && next.minimumLevel > next.maximumLevel) { noti.error('最低等级不能大于最高等级。'); return; }
    setPage(1); setSearch(next);
  }
  function filter(next: Filters) { setFilters(next); apply(next); }
  return <>
    <div className="mail-picker-filters">
      <Input aria-label="附件名称搜索" placeholder="搜索物品名称" value={filters.q} maxLength={128}
        onChange={event => setFilters({ ...filters, q: event.target.value })} onPressEnter={() => apply(filters)} />
      <Input aria-label="附件精确 ID" placeholder="精确物品 ID" value={filters.id} maxLength={10}
        onChange={event => setFilters({ ...filters, id: event.target.value })} onPressEnter={() => apply(filters)} />
      <Select aria-label="附件分类" placeholder="全部分类" allowClear value={filters.category}
        options={Object.entries(itemCategories).filter(([value]) => purpose !== 'daily-refill' || value !== 'equipment').map(([value, label]) => ({ value, label }))}
        onChange={category => filter({ ...filters, category, equipmentType: undefined, minimumLevel: undefined, maximumLevel: undefined, rarity: undefined })} />
      <Space><Button type="primary" onClick={() => apply(filters)}>搜索物品</Button><Button onClick={() => filter({ q: '', id: '' })}>重置筛选</Button></Space>
    </div>
    {filters.category === 'equipment' && <Space wrap className="mail-picker-extra-filters">
      <Select aria-label="装备部位" placeholder="全部装备部位" allowClear value={filters.equipmentType} style={{ width: 170 }}
        options={Object.entries(equipmentTypes).map(([value, label]) => ({ value, label }))} onChange={equipmentType => filter({ ...filters, equipmentType })} />
      <Select aria-label="装备稀有度" placeholder="全部稀有度" allowClear value={filters.rarity} style={{ width: 150 }}
        options={itemRarities.map((label, value) => ({ value, label }))} onChange={rarity => filter({ ...filters, rarity })} />
      <InputNumber aria-label="装备最低等级" placeholder="最低等级" min={0} max={999} precision={0} value={filters.minimumLevel}
        onChange={value => filter({ ...filters, minimumLevel: value ?? undefined })} />
      <span>—</span><InputNumber aria-label="装备最高等级" placeholder="最高等级" min={0} max={999} precision={0} value={filters.maximumLevel}
        onChange={value => filter({ ...filters, maximumLevel: value ?? undefined })} />
    </Space>}
    <div className="mail-picker-body">
      <section className="mail-picker-results" aria-label="物品检索结果">
        <Table<ItemCatalogEntry> rowKey="itemId" size="small" loading={results.busy} dataSource={results.data?.items ?? []} pagination={false}
          scroll={{ y: 430, x: 520 }} locale={{ emptyText: results.busy ? '正在加载物品目录…' : '没有符合条件的可发放物品' }}
          rowClassName={row => selectedIds.includes(row.itemId) ? 'mail-picker-selected' : ''}
          columns={[
            { title: '物品', width: 230, render: (_, row) => <><Typography.Text strong>{row.name}</Typography.Text><div className="mail-item-id">ID {row.itemId}</div></> },
            { title: '分类', width: 80, render: (_, row) => itemCategories[row.category] ?? row.category },
            { title: '等级', width: 60, render: (_, row) => row.level ?? '—' },
            { title: '稀有度', width: 75, render: (_, row) => row.rarity === null ? '—' : itemRarities[row.rarity] ?? row.rarity },
            { title: '', width: 76, render: (_, row) => <Button size="small" type={selectedIds.includes(row.itemId) ? 'primary' : 'default'}
              aria-label={`${multiple ? '添加' : '选择'}物品 ${row.itemId}`} onClick={() => onSelect(row)}>{multiple ? '添加' : '选择'}</Button> },
          ]} />
        <Space className="mail-picker-pagination"><Button disabled={page === 1 || results.busy} onClick={() => setPage(page - 1)}>上一页物品</Button>
          <Typography.Text>第 {page} 页</Typography.Text><Button disabled={!results.data?.hasMore || results.busy} onClick={() => setPage(page + 1)}>下一页物品</Button></Space>
      </section>
      {configuration}
    </div>
  </>;
}
