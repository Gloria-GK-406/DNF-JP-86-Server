import { useEffect, useState, type ReactNode } from 'react';
import { Button, Input, InputNumber, Segmented, Select } from 'antd';
import { SearchOutlined } from '@ant-design/icons';
import { ItemIcon, ItemName, useItemSource, type ItemBrief, type ItemPurpose } from './itemInfo';
import { equipmentTypes, itemCategories, itemRarities, type ItemCatalogEntry, type ItemCatalogPage } from './items';
import { useGmRead } from './useGmRead';

/* Catalog search shared by the GM add-items window (its left pane) and the player's mail page (inline).
   The query it sends is the GM one; only the source (prefix) differs, so the same selection rule applies everywhere. */
type Filters = { q: string; category: string; rarity?: number; equipmentType?: string; minimumLevel?: number | null; maximumLevel?: number | null };

export function ItemSearch({ purpose, noEquipment, autoFocus, requireQuery, countNote, emptyText, queued, tags, action, onExpired, onDenied }: {
  purpose: ItemPurpose; noEquipment?: boolean; autoFocus?: boolean; requireQuery?: boolean; countNote?: ReactNode;
  emptyText: (state: { busy: boolean; error: boolean }) => ReactNode;
  queued?: (entry: ItemCatalogEntry) => boolean; tags?: (entry: ItemCatalogEntry) => ReactNode; action: (entry: ItemCatalogEntry) => ReactNode;
  onExpired: () => void; onDenied: () => void }) {
  const source = useItemSource();
  const [filters, setFilters] = useState<Filters>({ q: '', category: 'all' });
  const [search, setSearch] = useState<Filters>({ q: '', category: 'all' });
  const [page, setPage] = useState(1);
  const params = new URLSearchParams({ page: String(page), pageSize: '30' });
  const text = search.q.trim();
  if (/^\d{1,10}$/.test(text) && +text > 0 && +text <= 2147483647) params.set('itemId', text); else if (text) params.set('q', text.slice(0, 128));
  if (search.category !== 'all') params.set('category', search.category);
  if (search.rarity !== undefined) params.set('rarity', String(search.rarity));
  if (search.category === 'equipment') {
    if (search.equipmentType) params.set('equipmentType', search.equipmentType);
    const inverted = search.minimumLevel != null && search.maximumLevel != null && search.minimumLevel > search.maximumLevel;
    if (search.minimumLevel != null && !inverted) params.set('minimumLevel', String(search.minimumLevel));
    if (search.maximumLevel != null && !inverted) params.set('maximumLevel', String(search.maximumLevel));
  }
  // Inline (requireQuery): nothing is listed until a name, ID or filter is given.
  const idle = !!requireQuery && !text && search.category === 'all' && search.rarity === undefined;
  const results = useGmRead<ItemCatalogPage>(idle ? null : `${purpose}/items?${params}`, onExpired, onDenied, source.read);
  useEffect(() => { const timer = window.setTimeout(() => { if (filters.q !== search.q) { setSearch(old => ({ ...old, q: filters.q })); setPage(1); } }, 300); return () => window.clearTimeout(timer); }, [filters.q]);
  function apply(next: Filters) { setFilters(next); setSearch(next); setPage(1); }
  const categories = Object.entries(itemCategories).filter(([key]) => !noEquipment || key !== 'equipment');
  const list = results.data?.items ?? [];
  return <>
    <div className="picker-filters">
      <Segmented size="small" value={filters.category} onChange={value => apply({ q: filters.q, category: String(value), rarity: filters.rarity })}
        options={[{ label: '全部', value: 'all' }, ...categories.map(([value, label]) => ({ value, label }))]} />
      <Input allowClear autoFocus={autoFocus} prefix={<SearchOutlined />} aria-label="物品搜索" placeholder="物品名称或精确 ID" maxLength={128} value={filters.q}
        onChange={event => setFilters({ ...filters, q: event.target.value })} onPressEnter={() => apply(filters)} style={{ flex: '1 1 200px', minWidth: 0 }} />
      <Select allowClear placeholder="稀有度" aria-label="稀有度" style={{ width: 110 }} value={filters.rarity} onChange={rarity => apply({ ...filters, rarity })}
        options={itemRarities.map((label, value) => ({ value, label: <span className={`r${value}`}>{label}</span> }))} />
      {filters.category === 'equipment' && <div className="picker-extra">
        <Select allowClear size="small" placeholder="全部部位" aria-label="装备部位" style={{ width: 120 }} value={filters.equipmentType} onChange={equipmentType => apply({ ...filters, equipmentType })}
          options={Object.entries(equipmentTypes).map(([value, label]) => ({ value, label }))} />
        <InputNumber size="small" placeholder="最低 Lv" aria-label="最低等级" min={0} max={999} precision={0} value={filters.minimumLevel} onChange={value => apply({ ...filters, minimumLevel: value })} style={{ width: 86 }} />
        <span className="faint">—</span>
        <InputNumber size="small" placeholder="最高 Lv" aria-label="最高等级" min={0} max={999} precision={0} value={filters.maximumLevel} onChange={value => apply({ ...filters, maximumLevel: value })} style={{ width: 86 }} />
        {filters.minimumLevel != null && filters.maximumLevel != null && filters.minimumLevel > filters.maximumLevel && <span className="err small">最低等级不能大于最高等级</span>}
      </div>}
    </div>
    {!idle && <><div className="picker-count"><span>{results.busy ? '正在检索…' : `第 ${page} 页 · ${list.length} 件${results.data?.hasMore ? '，还有更多' : ''}`}</span>
      {countNote && <span>{countNote}</span>}</div>
    <div className="picker-list" role="list" aria-label="物品检索结果">
      {list.length === 0 && <div className="empty-line">{emptyText({ busy: results.busy, error: !!results.error })}</div>}
      {list.map(entry => { const brief: ItemBrief = { name: entry.name, rarity: entry.rarity, category: entry.category, level: entry.level };
        return <div className={`pick-row ${queued?.(entry) ? 'queued' : ''}`} role="listitem" key={entry.itemId}>
          <ItemIcon itemId={entry.itemId} brief={brief} />
          <div className="pick-info"><ItemName itemId={entry.itemId} brief={brief} />
            <span className="meta">{itemCategories[entry.category] ?? entry.category}{entry.equipmentType ? ` · ${equipmentTypes[entry.equipmentType] ?? entry.equipmentType}` : ''}
              {entry.level !== null ? ` · Lv.${entry.level}` : ''}{entry.rarity !== null && <> · <span className={`r${entry.rarity}`}>{itemRarities[entry.rarity] ?? entry.rarity}</span></>}</span></div>
          <span className="pick-tags">{tags?.(entry)}</span>
          {action(entry)}
        </div>; })}
      {(page > 1 || results.data?.hasMore) && <div className="toolbar" style={{ justifyContent: 'center', borderBottom: 0 }}>
        <Button size="small" disabled={page === 1 || results.busy} onClick={() => setPage(page - 1)}>上一页</Button><span className="faint small">第 {page} 页</span>
        <Button size="small" disabled={!results.data?.hasMore || results.busy} onClick={() => setPage(page + 1)}>下一页</Button></div>}
    </div></>}
  </>;
}
