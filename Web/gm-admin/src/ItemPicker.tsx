import { useState } from 'react';
import { Button, Card, Empty, Input, InputNumber, Modal, Select, Space, Spin, Table, Tag, Typography } from 'antd';
import { useGmRead } from './useGmRead';
import { useNotiBar } from './NotiBar';
import { amplifyTypes, defaultEquipment, equipmentTypes, itemCategories, itemRarities, type ItemCatalogEntry, type ItemCatalogPage, type ItemEquipmentOptions, type ItemInput, type ItemCatalogDetails } from './items';

type AccessCallbacks = { onExpired: () => void; onDenied: () => void };
type Filters = { q: string; id: string; category?: string; equipmentType?: string; minimumLevel?: number; maximumLevel?: number; rarity?: number };

export function ItemPicker({ initial, onClose, onChoose, onExpired, onDenied, purpose }: AccessCallbacks & {
  purpose: 'mail' | 'initialization' | 'daily-refill'; initial?: ItemInput; onClose: () => void; onChoose: (item: ItemInput, details: ItemCatalogDetails) => string | undefined;
}) {
  const [filters, setFilters] = useState<Filters>({ q: '', id: '' });
  const [search, setSearch] = useState<Filters>({ q: '', id: '' });
  const [page, setPage] = useState(1); const [selected, setSelected] = useState<number | undefined>(initial?.itemId);
  const noti = useNotiBar();
  const params = new URLSearchParams({ page: String(page), pageSize: '15' });
  if (search.q.trim()) params.set('q', search.q.trim());
  if (search.id) params.set('itemId', search.id);
  for (const key of ['category', 'equipmentType', 'minimumLevel', 'maximumLevel', 'rarity'] as const)
    if (search[key] !== undefined) params.set(key, String(search[key]));
  const results = useGmRead<ItemCatalogPage>(`${purpose}/items?${params}`, onExpired, onDenied);
  const details = useGmRead<ItemCatalogDetails>(selected ? `${purpose}/items/${selected}` : null, onExpired, onDenied);
  function apply(next: Filters) {
    if (next.id && (!/^\d+$/.test(next.id) || +next.id <= 0 || +next.id > 2147483647)) { noti.error('物品 ID 必须为正整数。'); return; }
    if (next.minimumLevel !== undefined && next.maximumLevel !== undefined && next.minimumLevel > next.maximumLevel) { noti.error('最低等级不能大于最高等级。'); return; }
    setPage(1); setSearch(next);
  }
  function filter(next: Filters) { setFilters(next); apply(next); }
  return <Modal title={purpose === 'daily-refill' ? '选择每日补给物品' : purpose === 'initialization' ? (initial ? '编辑初始物品' : '添加初始物品') : (initial ? '编辑物品附件' : '添加物品附件')} open onCancel={onClose} footer={null}
    width={1160} className="mail-picker-modal" destroyOnHidden>
    <Typography.Paragraph type="secondary">{purpose === 'daily-refill' ? '选择每日补给的永久普通背包堆叠物品。' : purpose === 'initialization' ? '选择额外赠送的永久普通背包物品，装备使用模板默认状态。' : '从当前游戏物品目录检索，选择物品后配置数量与装备状态。仅显示邮件支持发放的永久物品。'}</Typography.Paragraph>
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
          rowClassName={row => row.itemId === selected ? 'mail-picker-selected' : ''}
          columns={[
            { title: '物品', width: 230, render: (_, row) => <><Typography.Text strong>{row.name}</Typography.Text><div className="mail-item-id">ID {row.itemId}</div></> },
            { title: '分类', width: 80, render: (_, row) => itemCategories[row.category] ?? row.category },
            { title: '等级', width: 60, render: (_, row) => row.level ?? '—' },
            { title: '稀有度', width: 75, render: (_, row) => row.rarity === null ? '—' : itemRarities[row.rarity] ?? row.rarity },
            { title: '', width: 76, render: (_, row) => <Button size="small" type={selected === row.itemId ? 'primary' : 'default'}
              aria-label={`选择物品 ${row.itemId}`} onClick={() => setSelected(row.itemId)}>选择</Button> },
          ]} />
        <Space className="mail-picker-pagination"><Button disabled={page === 1 || results.busy} onClick={() => setPage(page - 1)}>上一页物品</Button>
          <Typography.Text>第 {page} 页</Typography.Text><Button disabled={!results.data?.hasMore || results.busy} onClick={() => setPage(page + 1)}>下一页物品</Button></Space>
      </section>
      <Card size="small" title={purpose === 'daily-refill' ? '选择补给数量' : purpose === 'initialization' ? '配置初始物品' : '配置附件'} className="mail-picker-config">
        {!selected && <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="先从左侧选择一个物品" />}
        {details.busy && <div className="mail-picker-loading"><Spin /><Typography.Paragraph>正在读取物品规则…</Typography.Paragraph></div>}
        {details.error && <Button onClick={details.refresh}>重试</Button>}
        {details.data && <ItemConfiguration key={details.data.item.itemId} details={details.data} simple={purpose !== 'mail'} refill={purpose === 'daily-refill'}
          initial={initial?.itemId === details.data.item.itemId ? initial : undefined}
          onChoose={item => { const refusal = onChoose(item, details.data!); if (refusal) noti.error(refusal); }} />}
      </Card>
    </div>
  </Modal>;
}

function ItemConfiguration({ details, initial, onChoose, simple = false, refill = false }: { simple?: boolean; refill?: boolean; details: ItemCatalogDetails; initial?: ItemInput; onChoose: (item: ItemInput) => void }) {
  const [count, setCount] = useState<number | null>(initial?.count ?? 1);
  const [options, setOptions] = useState<ItemEquipmentOptions>(initial?.equipment ?? defaultEquipment());
  const capabilities = details.equipment;
  const isEquipment = details.kind === 'equipment';
  const maximum = options.growth === 'amplification' ? capabilities?.maxAmplificationLevel ?? 0 : capabilities?.maxReinforcementLevel ?? 0;
  const valid = count !== null && Number.isInteger(count) && count > 0 && count <= (isEquipment && !simple ? 10 : 2147483647)
    && (!isEquipment || options.growth === 'none' || Number.isInteger(options.level) && options.level >= 0 && options.level <= maximum
      && (options.growth === 'reinforcement' ? maximum > 0 && options.level >= 1 : capabilities?.canAmplify && !!amplifyTypes[options.amplifyType]));
  return <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
    <div><Typography.Title level={5} style={{ margin: 0 }}>{details.item.name}</Typography.Title><Typography.Text type="secondary">ID {details.item.itemId}</Typography.Text></div>
    <Space wrap><Tag>{itemCategories[details.item.category]}</Tag>{details.item.equipmentType && <Tag>{equipmentTypes[details.item.equipmentType] ?? details.item.equipmentType}</Tag>}
      {details.item.level !== null && <Tag>Lv. {details.item.level}</Tag>}{details.item.rarity !== null && <Tag>{itemRarities[details.item.rarity]}</Tag>}</Space>
    <div><label htmlFor="attachment-count">{refill ? '补给数量' : simple ? '每角色数量' : '每人数量'}</label><InputNumber id="attachment-count" style={{ width: '100%', marginTop: 6 }}
      min={1} max={isEquipment && !simple ? 10 : 2147483647} precision={0} value={count} onChange={setCount} /></div>
    {isEquipment && !simple && <>
      <div><label htmlFor="attachment-seal">封装状态</label><Select id="attachment-seal" aria-label="封装状态" style={{ width: '100%', marginTop: 6 }} value={options.seal}
        options={[{ value: 'default', label: `模板默认（${details.defaultSealed ? '封装' : '未封装'}）` }, { value: 'unsealed', label: '解除封装' }]}
        onChange={seal => setOptions({ ...options, seal })} /></div>
      <div><label htmlFor="attachment-growth">成长模式</label><Select id="attachment-growth" aria-label="成长模式" style={{ width: '100%', marginTop: 6 }} value={options.growth}
        options={[{ value: 'none', label: '无强化 / 增幅' }, { value: 'reinforcement', label: '强化', disabled: !capabilities?.maxReinforcementLevel },
          { value: 'amplification', label: '增幅', disabled: !capabilities?.canAmplify }]}
        onChange={growth => setOptions({ ...options, growth, level: growth === 'reinforcement' ? 1 : 0, amplifyType: growth === 'amplification' ? 3 : 0 })} /></div>
      {capabilities?.reinforcementReason && <Typography.Text type="secondary">{capabilities.reinforcementReason}</Typography.Text>}
      {capabilities?.amplificationReason && <Typography.Text type="secondary">{capabilities.amplificationReason}</Typography.Text>}
      {options.growth !== 'none' && <div><label htmlFor="attachment-level">{options.growth === 'reinforcement' ? '强化等级' : '增幅等级'}</label>
        <InputNumber id="attachment-level" style={{ width: '100%', marginTop: 6 }} min={options.growth === 'reinforcement' ? 1 : 0} max={maximum} precision={0}
          value={options.level} onChange={level => setOptions({ ...options, level: level ?? 0 })} /><Typography.Text type="secondary">此装备最高 +{maximum}</Typography.Text></div>}
      {options.growth === 'amplification' && <div><label htmlFor="attachment-amplify">异次元属性</label>
        <Select id="attachment-amplify" aria-label="异次元属性" style={{ width: '100%', marginTop: 6 }} value={options.amplifyType}
          options={[3, 4, 1, 2].map(value => ({ value, label: amplifyTypes[value] }))} onChange={amplifyType => setOptions({ ...options, amplifyType })} />
        <Typography.Text type="secondary">基础异次元属性值 {capabilities?.amplifyValues[options.amplifyType]}，由服务端按模板规则生成。</Typography.Text></div>}
      <Typography.Text type="secondary">每件装备占一个附件位，具有独立身份。</Typography.Text>
    </>}
    <Button type="primary" block disabled={!valid} onClick={() => {
      const equipment = isEquipment && (options.seal !== 'default' || options.growth !== 'none') ? { ...options } : undefined;
      onChoose({ itemId: details.item.itemId, count: count!, ...(equipment ? { equipment } : {}) });
    }}>{refill ? '选择此补给物品' : simple ? (initial ? '保存物品配置' : '加入初始物品') : (initial ? '保存附件配置' : '加入邮件')}</Button>
  </Space>;
}
