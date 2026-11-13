import { useRef, useState } from 'react';
import { Button, InputNumber, Modal, Tooltip } from 'antd';
import { CloseOutlined, PlusOutlined } from '@ant-design/icons';
import { equipError, EquipEditor } from './EquipEditor';
import { briefOf, ItemIcon, ItemName, ItemSourceContext, useItemDetails, useItemSource, type ItemPurpose, type ItemSource } from './itemInfo';
import { ItemSearch } from './ItemSearch';
import { defaultEquip, equipOptionsOf, mergeItems, usedSlots, type EquipState, type ItemCatalogDetails, type ItemCatalogEntry, type ItemInput } from './items';

/* The standalone add-items window: search on the left, configure each queued item on the right, add them all at once.
   mail / template: equipment configurable, each piece takes one of 10 slots. init: one row per item, count only, 10 kinds.
   refill: pick exactly one non-equipment item. */
export type PickerPurpose = 'mail' | 'template' | 'init' | 'refill';
type Card = { key: number; entry: ItemCatalogEntry; count: number | null; equip: EquipState };
const apiPurpose: Record<PickerPurpose, ItemPurpose> = { mail: 'mail', template: 'mail', init: 'initialization', refill: 'daily-refill' };
const titles: Record<PickerPurpose, [string, string, string]> = {
  mail: ['添加邮件附件', '只列能用邮件发的永久物品', '加入邮件'], template: ['添加模板附件', '只列能用邮件发的永久物品', '加入模板'],
  init: ['添加初始物品', '永久普通物品，装备直接放进背包', '加入初始物品'], refill: ['选择补给物品', '每日补给只支持非装备物品', '就用它'],
};

export function ItemPicker({ purpose, existing = [], known = {}, source: given, onClose, onConfirm, onExpired, onDenied }: { purpose: PickerPurpose; existing?: ItemInput[];
  known?: Record<number, ItemCatalogDetails | undefined>; source?: ItemSource; onClose: () => void; onConfirm: (adds: ItemInput[], details: Record<number, ItemCatalogDetails>) => string | undefined;
  onExpired: () => void; onDenied: () => void }) {
  const context = useItemSource(); const source = given ?? context;
  const access = { onExpired, onDenied };
  const single = purpose === 'refill'; const typesOnly = purpose === 'init'; const slotted = purpose === 'mail' || purpose === 'template';
  const [queue, setQueue] = useState<Card[]>([]); const nextKey = useRef(0);
  const [refusal, setRefusal] = useState<string>();
  const queued = useItemDetails(apiPurpose[purpose], queue.map(card => card.entry.itemId), access, source);
  const existingDetails = useItemDetails(apiPurpose[purpose], existing.map(item => item.itemId).filter(id => !known[id]), access, source);
  const details: Record<number, ItemCatalogDetails | undefined> = { ...known, ...existingDetails.details, ...queued.details };
  const equipment = (card: Card) => details[card.entry.itemId]?.kind === 'equipment';
  function cardError(card: Card) {
    const loaded = details[card.entry.itemId];
    if (!loaded) return queued.failed[card.entry.itemId] ? '规则读取失败' : '';
    if (single) return '';
    if (card.count === null || !Number.isInteger(card.count) || card.count < 1) return '数量至少 1';
    if (card.count > 2147483647) return '数量超出范围';
    if (slotted && loaded.kind === 'equipment' && card.count > 10) return '装备每件占一格，一次最多 10 件';
    return slotted && loaded.kind === 'equipment' ? equipError(loaded, card.equip) : '';
  }
  const inputs: ItemInput[] = queue.map(card => ({ itemId: card.entry.itemId, count: single ? 1 : card.count ?? 0,
    ...(slotted && equipment(card) ? { equipment: equipOptionsOf(card.equip) } : {}) }));
  const ready = queue.length > 0 && queue.every(card => !!details[card.entry.itemId]) && existing.every(item => !!details[item.itemId]);
  const errors = queue.map(cardError).filter(Boolean).length;
  let merged: ItemInput[] = []; let capacity = '';
  if (ready && !errors && !single) {
    try { merged = mergeItems(existing, inputs); } catch (failure) { capacity = (failure as Error).message; }
    if (!capacity && slotted && (merged.length > 10 || usedSlots(merged, details) > 10)) capacity = '超过 10 格了：每件装备单独占一个附件位';
    if (!capacity && typesOnly && merged.length > 10) capacity = '超过 10 种了';
  }
  const inExisting = (id: number) => existing.filter(item => item.itemId === id).length;
  const inQueue = (id: number) => queue.filter(card => card.entry.itemId === id).length;
  function add(entry: ItemCatalogEntry) {
    setRefusal(undefined);
    if (single) { setQueue([{ key: nextKey.current++, entry, count: 1, equip: defaultEquip() }]); return; }
    if (typesOnly && (inExisting(entry.itemId) || inQueue(entry.itemId))) return;
    setQueue(old => [...old, { key: nextKey.current++, entry, count: 1, equip: defaultEquip() }]);
  }
  const setCard = (key: number, patch: Partial<Card>) => { setRefusal(undefined); setQueue(old => old.map(card => card.key === key ? { ...card, ...patch } : card)); };
  function confirm() {
    if (!ready || errors || capacity) return;
    const loaded = Object.fromEntries(Object.entries(details).filter(([, value]) => !!value)) as Record<number, ItemCatalogDetails>;
    const error = onConfirm(inputs, loaded);
    if (error) setRefusal(error); else onClose();
  }
  const [title, hint, confirmText] = titles[purpose];
  const used = slotted && merged.length ? usedSlots(merged, details) : slotted ? usedSlots(existing, details) : 0;
  const kinds = new Set((merged.length ? merged : existing).map(item => item.itemId)).size;
  const footNote = single ? (queue.length ? `已选：${queue[0].entry.name}` : '从左边选一件')
    : slotted ? <span>加入后每人占用 <b className="mono" style={{ color: capacity ? 'var(--crit)' : undefined }}>{used}</b> / 10 格 · 相同物品与配置会合并数量</span>
      : <span>加入后共 <b className="mono" style={{ color: capacity ? 'var(--crit)' : undefined }}>{kinds}</b> / 10 种</span>;
  return <ItemSourceContext.Provider value={source}><Modal open onCancel={onClose} width={1180} centered destroyOnHidden className="picker-modal" mask={{ closable: false }}
    title={<span>{title} <span className="faint small" style={{ fontWeight: 400, marginInlineStart: 8 }}>{hint}</span></span>}
    footer={<div className="picker-foot"><span className="grow">{footNote}
      {!ready && queue.length > 0 && <span className="faint" style={{ marginInlineStart: 10 }}>正在核对物品规则…</span>}
      {errors > 0 && <span style={{ color: 'var(--crit)', marginInlineStart: 10 }}>{errors} 项配置需要修正</span>}
      {(capacity || refusal) && <span style={{ color: 'var(--crit)', marginInlineStart: 10 }} role="alert">{capacity || refusal}</span>}</span>
      <Button onClick={onClose}>取消</Button>
      <Button type="primary" disabled={!ready || errors > 0 || !!capacity} onClick={confirm}>{single ? confirmText : `${confirmText}（${queue.length} 项）`}</Button></div>}>
    <div className="picker">
      <section className="picker-left" aria-label="物品检索">
        <ItemSearch purpose={apiPurpose[purpose]} noEquipment={single} autoFocus {...access}
          countNote={`点“${single ? '选它' : '添加'}”放到右边${!single && !typesOnly ? '，同一件可以加多次配成不同状态' : ''}`}
          emptyText={({ busy, error }) => busy ? '正在加载物品目录…' : error ? '目录读取失败' : '没有符合条件的物品，换个关键词试试'}
          queued={entry => inQueue(entry.itemId) > 0}
          tags={entry => { const had = inExisting(entry.itemId); const count = inQueue(entry.itemId);
            return <>{had > 0 && <span className="chip">已在列表 {had}</span>}{count > 0 && <span className="chip accent">待加入 {count}</span>}</>; }}
          action={entry => { const count = inQueue(entry.itemId); const blocked = typesOnly && (inExisting(entry.itemId) > 0 || count > 0);
            return <Button size="small" type={count && single ? 'primary' : 'default'} disabled={blocked} icon={single ? undefined : <PlusOutlined />}
              aria-label={`${single ? '选择' : '添加'}物品 ${entry.itemId}`} onClick={() => add(entry)}>{single ? (count ? '已选' : '选它') : '添加'}</Button>; }} />
      </section>
      <section className="picker-right" aria-label="待加入">
        <div className="picker-right-head"><b>{single ? '已选物品' : `待加入（${queue.length}）`}</b>{queue.length > 0 && !single && <button className="linkbtn" onClick={() => setQueue([])}>全部清空</button>}</div>
        <div className="picker-queue">
          {queue.length === 0 && <div className="empty-line">{single ? '从左边选一件补给物品' : '从左边点“添加”，在这里调数量和装备状态'}</div>}
          {queue.map((card, index) => { const loaded = details[card.entry.itemId]; const error = cardError(card);
            const brief = briefOf(loaded) ?? { name: card.entry.name, rarity: card.entry.rarity };
            return <div className={`qcard ${error ? 'bad' : ''}`} key={card.key} aria-label={`待加入 ${index + 1}`}>
              <div className="qcard-head"><ItemIcon itemId={card.entry.itemId} brief={brief} size="sm" equip={slotted && loaded?.kind === 'equipment' ? equipOptionsOf(card.equip) : null} capabilities={loaded?.equipment} />
                <div style={{ minWidth: 0, flex: 1 }}><ItemName itemId={card.entry.itemId} brief={brief} /></div>
                {!single && <Tooltip title="移除"><Button size="small" type="text" danger aria-label={`移除待加入 ${index + 1}`} icon={<CloseOutlined />} onClick={() => setQueue(old => old.filter(x => x.key !== card.key))} /></Tooltip>}</div>
              {!loaded && (queued.failed[card.entry.itemId] ? <span className="small">规则读取失败 <button className="linkbtn" onClick={queued.retry}>重试读取规则</button></span> : <span className="faint small">正在读取物品规则…</span>)}
              {loaded && !single && <div className="ee-row"><label className="ee-label" htmlFor={`picker-count-${card.key}`}>数量</label>
                <InputNumber id={`picker-count-${card.key}`} aria-label={`数量 ${index + 1}`} size="small" min={1} max={slotted && loaded.kind === 'equipment' ? 10 : 2147483647} precision={0} value={card.count}
                  onChange={value => setCard(card.key, { count: value })} suffix={purpose === 'init' ? '/角色' : '/人'} style={{ width: 140 }} />
                {slotted && loaded.kind === 'equipment' && <span className="faint small">占 {card.count ?? 0} 格</span>}</div>}
              {loaded && slotted && loaded.kind === 'equipment' && <EquipEditor details={loaded} value={card.equip} onChange={equip => setCard(card.key, { equip })} idPrefix={`picker-${card.key}`} />}
              {single && <span className="faint small">数量、方式和截止时间回到补给表单里填。</span>}
              {error && <span className="err small">{error}</span>}
            </div>; })}
        </div>
      </section>
    </div>
  </Modal></ItemSourceContext.Provider>;
}
