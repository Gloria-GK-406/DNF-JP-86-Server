import { createContext, useContext, useEffect, useReducer, useState, type ReactElement } from 'react';
import { Tooltip } from 'antd';
import { api, ApiError } from './api';
import { nf } from './format';
import { quietApi } from './quietApi';
import { amplifyTypes, equipStateOf, itemCategories, itemRarities, nameInitial, type ItemCatalogDetails, type ItemEquipmentCapabilities, type ItemEquipmentOptions } from './items';

/* Item widgets: icon tile (icon pack or name initial), game-style tooltip, and a shared catalog-detail cache.
   Static PVF facts never change while the page is open, so caches live at module level. */
export type ItemPurpose = 'mail' | 'initialization' | 'daily-refill';
export type ItemTooltip = { itemId: number; name: string; rarity: number | null; category: string; typeName: string; minimumLevel: number | null; maximumLevel: number | null;
  jobs: string[] | null; attach: string | null; periodDays: number | null; dailyDelete: boolean; stats: { label: string; value: string }[];
  explain: string | null; flavor: string | null; durability: number | null; weight: number | null; price: number | null; stackLimit: number | null };
/** What a caller already knows about an item, used until (or instead of) the tooltip data. */
export type ItemBrief = { name?: string | null; rarity?: number | null; category?: string | null; level?: number | null };

const detailCache = new Map<string, ItemCatalogDetails>();
const detailInflight = new Map<string, Promise<ItemCatalogDetails>>();
const tooltipCache = new Map<number, ItemTooltip | null>();
const tooltipInflight = new Map<number, Promise<ItemTooltip | null>>();
const failedIcons = new Set<number>();
export function resetItemCaches() { detailCache.clear(); detailInflight.clear(); tooltipCache.clear(); tooltipInflight.clear(); failedIcons.clear(); }
export const cachedDetails = (purpose: ItemPurpose, itemId: number) => detailCache.get(`${purpose}:${itemId}`);
export function rememberDetails(purpose: ItemPurpose, details: ItemCatalogDetails) { detailCache.set(`${purpose}:${details.item.itemId}`, details); }
export function loadDetails(purpose: ItemPurpose, itemId: number) {
  const key = `${purpose}:${itemId}`;
  const cached = detailCache.get(key); if (cached) return Promise.resolve(cached);
  let pending = detailInflight.get(key);
  if (!pending) {
    pending = api<ItemCatalogDetails>(`${purpose}/items/${itemId}`).then(value => { detailCache.set(key, value); return value; })
      .finally(() => detailInflight.delete(key));
    detailInflight.set(key, pending);
  }
  return pending;
}
type Access = { onExpired: () => void; onDenied: () => void };
/** Catalog details (name, kind, equipment capabilities) for a set of item IDs under one selection rule. */
export function useItemDetails(purpose: ItemPurpose, ids: number[], access: Access) {
  const [, rerender] = useReducer((value: number) => value + 1, 0);
  const [failed, setFailed] = useState<Record<number, string>>({});
  const [attempt, setAttempt] = useState(0);
  const unique = [...new Set(ids)].sort((a, b) => a - b); const key = unique.join(',');
  useEffect(() => {
    let active = true;
    for (const id of unique) {
      if (detailCache.has(`${purpose}:${id}`)) continue;
      loadDetails(purpose, id).then(() => { if (active) { setFailed(old => { if (!(id in old)) return old; const next = { ...old }; delete next[id]; return next; }); rerender(); } })
        .catch(failure => {
          if (!active) return;
          if (failure instanceof ApiError && failure.status === 401) access.onExpired();
          else if (failure instanceof ApiError && failure.status === 403) access.onDenied();
          else setFailed(old => ({ ...old, [id]: failure instanceof Error ? failure.message : '读取失败' }));
        });
    }
    return () => { active = false; };
  }, [purpose, key, attempt]);
  const details: Record<number, ItemCatalogDetails | undefined> = {};
  for (const id of unique) details[id] = detailCache.get(`${purpose}:${id}`);
  return { details, failed, ready: unique.every(id => !!details[id]), retry: () => { setFailed({}); setAttempt(value => value + 1); } };
}
function loadTooltip(itemId: number) {
  if (tooltipCache.has(itemId)) return Promise.resolve(tooltipCache.get(itemId) ?? null);
  let pending = tooltipInflight.get(itemId);
  if (!pending) {
    pending = quietApi<ItemTooltip>(`items/${itemId}/tooltip`).then(value => { tooltipCache.set(itemId, value ?? null); return value ?? null; })
      .finally(() => tooltipInflight.delete(itemId));
    tooltipInflight.set(itemId, pending);
  }
  return pending;
}

/** Whether the deployment has an icon pack; unknown until the shell has asked once. */
export const IconPackContext = createContext<boolean | undefined>(undefined);
export function useIconPackStatus() {
  const [available, setAvailable] = useState<boolean | undefined>(undefined);
  useEffect(() => {
    let active = true;
    quietApi<{ available: boolean }>('items/icon-pack').then(value => { if (active) setAvailable(!!value?.available); });
    return () => { active = false; };
  }, []);
  return available;
}
function IconFace({ itemId, name, size }: { itemId: number; name: string; size: number }) {
  const pack = useContext(IconPackContext); const [, rerender] = useReducer((value: number) => value + 1, 0);
  if (pack && !failedIcons.has(itemId))
    return <img src={`/api/gm/v1/items/${itemId}/icon`} alt="" width={size} height={size} loading="lazy" onError={() => { failedIcons.add(itemId); rerender(); }} />;
  return <span className="ini" aria-hidden="true">{nameInitial(name)}</span>;
}
export const rarityClass = (rarity: number | null | undefined) => `s${rarity ?? 0}`;
export function ItemIcon({ itemId, brief, equip, capabilities, size }: { itemId: number; brief?: ItemBrief; equip?: ItemEquipmentOptions | null;
  capabilities?: ItemEquipmentCapabilities | null; size?: 'sm' }) {
  const name = brief?.name ?? `物品 ${itemId}`;
  return <TipWrap itemId={itemId} brief={brief} equip={equip} capabilities={capabilities}>
    <span className={`tile ${size ?? ''} ${rarityClass(brief?.rarity)}`} tabIndex={0} aria-label={`${name} 的说明`}><IconFace itemId={itemId} name={name} size={28} /></span>
  </TipWrap>;
}
/** Slot face inside the game mail window (fixed dark slot, icon or initial). */
export function SlotFace({ itemId, name }: { itemId: number; name: string }) { return <IconFace itemId={itemId} name={name} size={28} />; }
export function ItemName({ itemId, brief, showId = true }: { itemId: number; brief?: ItemBrief; showId?: boolean }) {
  return <span><span className={brief?.rarity != null ? `r${brief.rarity}` : undefined} style={{ fontWeight: 600 }}>{brief?.name ?? `物品 ${itemId}`}</span>
    {showId && <span className="id" style={{ marginInlineStart: 6 }}>{itemId}</span>}</span>;
}
export const briefOf = (details?: ItemCatalogDetails): ItemBrief | undefined => details && { name: details.item.name, rarity: details.item.rarity, category: details.item.category, level: details.item.level };

export function TipWrap({ itemId, brief, equip, capabilities, children }: { itemId: number; brief?: ItemBrief; equip?: ItemEquipmentOptions | null;
  capabilities?: ItemEquipmentCapabilities | null; children: ReactElement }) {
  return <Tooltip title={() => <ItemCard itemId={itemId} brief={brief} equip={equip} capabilities={capabilities} />} color="transparent" arrow={false}
    placement="rightTop" mouseEnterDelay={0.12} destroyOnHidden styles={{ container: { padding: 0, background: 'transparent', boxShadow: 'none' } }}>{children}</Tooltip>;
}
const attachText: Record<string, [string, 'ok' | 'seal' | 'bad']> = { free: ['可以交易', 'ok'], sealing: ['封装', 'seal'], trade: ['无法交易', 'bad'],
  'trade delete': ['无法交易 · 无法删除', 'bad'], account: ['账号绑定', 'bad'] };
/* 说明框不计算品级、强化、增幅、锻造带来的附加数值：属性按 PVF 原值显示，成长只显示状态。 */
export function ItemCard({ itemId, brief, equip, capabilities }: { itemId: number; brief?: ItemBrief; equip?: ItemEquipmentOptions | null; capabilities?: ItemEquipmentCapabilities | null }) {
  const [tip, setTip] = useState<ItemTooltip | null | undefined>(() => tooltipCache.has(itemId) ? tooltipCache.get(itemId) ?? null : undefined);
  useEffect(() => { let active = true; if (tip === undefined) loadTooltip(itemId).then(value => { if (active) setTip(value); }); return () => { active = false; }; }, [itemId, tip]);
  const name = tip?.name ?? brief?.name ?? `物品 ${itemId}`; const rarity = tip?.rarity ?? brief?.rarity ?? 0;
  const state = equip ? equipStateOf(equip) : null; const grow = state && state.growth !== 'none' ? state.level : 0;
  const sealed = tip?.attach === 'sealing' && state?.seal !== 'unsealed';
  const attach = tip?.attach ? attachText[tip.attach] : undefined;
  const sockets = state?.socket ? capabilities?.socketCount ?? 0 : 0;
  return <div className="dtip" role="tooltip">
    <div className="dtip-head">
      <span className={`tile ${rarityClass(rarity)}`}><IconFace itemId={itemId} name={name} size={28} /></span>
      <div style={{ minWidth: 0 }}><div className={`dtip-name ${rarityClass(rarity)}`}>{grow ? `+${grow} ` : ''}{name}</div>
        <div className="dtip-sub">{itemRarities[rarity] ?? ''} {tip?.typeName ?? (brief?.category ? itemCategories[brief.category] ?? '' : '')}</div></div>
    </div>
    {tip === undefined && <div className="dtip-sec dim2">正在读取物品说明…</div>}
    {tip === null && <div className="dtip-sec dim2">{brief?.level ? `Lv${brief.level} 以上可以使用 · ` : ''}暂无更多说明</div>}
    {tip && <div className="dtip-sec">
      {tip.minimumLevel !== null && <div className="dim2">Lv{tip.minimumLevel}{tip.maximumLevel ? ` ~ Lv${tip.maximumLevel}` : ''} 以上可以使用</div>}
      {tip.category === 'equipment' && <div className="dim2">{tip.jobs?.length ? tip.jobs.join('、') : '全职业'}</div>}
      {tip.attach === 'sealing' ? <div className={sealed ? 'att-seal' : 'att-bad'}>{sealed ? '封装' : '已解除封装（无法交易）'}</div>
        : attach ? <div className={`att-${attach[1]}`}>{attach[0]}</div> : null}
      {tip.periodDays ? <div className="att-bad">使用期限 {tip.periodDays} 天</div> : null}
      {tip.dailyDelete && <div className="att-bad">每天 6:00 删除</div>}
    </div>}
    {tip && tip.stats.length > 0 && <div className="dtip-sec">{tip.stats.map((stat, index) => <div className="stat-line" key={index}><span>{stat.label}</span><span>{stat.value}</span></div>)}</div>}
    {state && (grow || state.forge || sockets || state.quality !== 4) ? <div className="dtip-sec">
      {state.growth === 'reinforcement' && <div className="g-reinf">强化 +{state.level}</div>}
      {state.growth === 'amplification' && <div className="g-amp">增幅 +{state.level} · 异次元{amplifyTypes[state.amplifyType] ?? ''}</div>}
      {state.forge > 0 && <div className="g-forge">锻造 +{state.forge}</div>}
      {sockets > 0 && <div className="g-sock">徽章孔 <span className="holes">{Array.from({ length: sockets }, (_, index) => <i key={index} />)}</span> {capabilities?.socketClass ?? ''} 类 · 空</div>}
    </div> : null}
    {tip && (tip.explain || tip.flavor) && <div className="dtip-sec">{tip.explain && <div className="explain">{tip.explain}</div>}{tip.flavor && <div className="flavor">{tip.flavor}</div>}</div>}
    {tip && <div className="dtip-foot">
      <span>{tip.durability ? `耐久度 ${tip.durability}/${tip.durability}` : tip.stackLimit && tip.stackLimit > 1 ? `最多叠加 ${nf(tip.stackLimit)} 个` : ''}</span>
      <span>{tip.weight ? `${tip.weight >= 0.1 ? tip.weight.toFixed(1) : tip.weight}kg` : ''}{tip.price ? ` · 出售 ${nf(tip.price)} 金币` : ''}</span>
    </div>}
  </div>;
}
