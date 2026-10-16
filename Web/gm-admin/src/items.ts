/** Wire shape of a configured equipment piece. Defaults are omitted so unchanged pieces keep the pre-extension JSON. */
export type ItemEquipmentOptions = { seal: 'default' | 'unsealed'; growth: 'none' | 'reinforcement' | 'amplification'; level: number; amplifyType: number;
  forge?: number; socket?: boolean; quality?: number | null };
export type ItemInput = { itemId: number; count: number; equipment?: ItemEquipmentOptions | null };
export type ItemCatalogEntry = { itemId: number; name: string; category: string; equipmentType: string | null; level: number | null; rarity: number | null };
export type ItemCatalogPage = { items: ItemCatalogEntry[]; page: number; pageSize: number; hasMore: boolean };
export type ItemEquipmentCapabilities = { maxReinforcementLevel: number; maxAmplificationLevel: number; canAmplify: boolean;
  reinforcementReason: string | null; amplificationReason: string | null; amplifyValues: Record<string, number>;
  maxForgeLevel?: number; socketCount?: number; socketClass?: string | null };
export type ItemCatalogDetails = { item: ItemCatalogEntry; kind: string; defaultSealed: boolean; durability: number; equipment: ItemEquipmentCapabilities | null };

/** Editor state for one equipment piece; every field present. quality 4 (最上级) is the default tier. */
export type EquipState = { seal: 'default' | 'unsealed'; growth: 'none' | 'reinforcement' | 'amplification'; level: number; amplifyType: number;
  forge: number; socket: boolean; quality: number };
export const defaultEquip = (): EquipState => ({ seal: 'default', growth: 'none', level: 0, amplifyType: 0, forge: 0, socket: false, quality: 4 });
export function equipStateOf(options?: ItemEquipmentOptions | null): EquipState {
  return { ...defaultEquip(), ...(options ?? {}), forge: options?.forge ?? 0, socket: options?.socket ?? false, quality: options?.quality ?? 4 };
}
export function equipOptionsOf(state: EquipState): ItemEquipmentOptions | null {
  const options: ItemEquipmentOptions = { seal: state.seal, growth: state.growth, level: state.growth === 'none' ? 0 : state.level,
    amplifyType: state.growth === 'amplification' ? state.amplifyType : 0 };
  if (state.forge > 0) options.forge = state.forge;
  if (state.socket) options.socket = true;
  if (state.quality !== 4) options.quality = state.quality;
  return options.seal === 'default' && options.growth === 'none' && options.forge === undefined && options.socket === undefined && options.quality === undefined ? null : options;
}
export const equipKey = (options?: ItemEquipmentOptions | null) => {
  const value = options ? equipOptionsOf(equipStateOf(options)) : null;
  return value ? JSON.stringify([value.seal, value.growth, value.level, value.amplifyType, value.forge ?? 0, value.socket ?? false, value.quality ?? 4]) : '';
};

/* 品级：装备 ItemCore.Value 是属性种子。GM 只选档位，服务端写入该档的固定种子；
   客户端 0x00AA6E60 对固定种子取 p = (档位 + 1) × 20，即每档上限，不走随机分支。 */
export const qualityTiers = [
  { value: 0, label: '最下级', pct: 20 }, { value: 1, label: '下级', pct: 40 }, { value: 2, label: '中级', pct: 60 },
  { value: 3, label: '上级', pct: 80 }, { value: 4, label: '最上级', pct: 100 },
];
export const itemCategories: Record<string, string> = { equipment: '装备', material: '材料', consumable: '消耗品', quest: '任务物品', other: '其他' };
export const equipmentTypes: Record<string, string> = { weapon: '武器', coat: '上衣', shoulder: '头肩', pants: '下装', shoes: '鞋', waist: '腰带',
  amulet: '项链', wrist: '手镯', ring: '戒指', support: '辅助装备', 'magic stone': '魔法石', 'title name': '称号' };
export const itemRarities = ['普通', '高级', '稀有', '神器', '史诗', '勇者', '传说'];
export const amplifyTypes: Record<number, string> = { 3: '力量', 4: '智力', 1: '体力', 2: '精神' };

export function equipText(options?: ItemEquipmentOptions | null) {
  if (!options) return '';
  const state = equipStateOf(options);
  const growth = state.growth === 'reinforcement' ? `+${state.level} 强化` : state.growth === 'amplification' ? `+${state.level} 增幅·${amplifyTypes[state.amplifyType] ?? '未知'}` : '';
  return [state.quality !== 4 ? qualityTiers[state.quality]?.label ?? '' : '', growth, state.forge ? `锻造 +${state.forge}` : '',
    state.socket ? '已开孔' : '', state.seal === 'unsealed' ? '已解封' : ''].filter(Boolean).join(' · ');
}
/** Same item + same equipment state merges counts; different states stay separate rows. */
export function mergeItems(existing: ItemInput[], adds: ItemInput[]): ItemInput[] {
  const out = existing.map(item => ({ ...item }));
  for (const add of adds) {
    const key = equipKey(add.equipment);
    const hit = out.find(item => item.itemId === add.itemId && equipKey(item.equipment) === key);
    if (hit) {
      const count = hit.count + add.count;
      if (!Number.isSafeInteger(count) || count > 2147483647) throw new Error('合并后的数量超出允许范围。');
      hit.count = count;
    } else out.push({ itemId: add.itemId, count: add.count, ...(key ? { equipment: equipOptionsOf(equipStateOf(add.equipment)) } : {}) });
  }
  return out;
}
/** Each equipment piece occupies its own attachment slot; a stack occupies one. */
export const usedSlots = (items: ItemInput[], known: Record<number, ItemCatalogDetails | undefined>) =>
  items.reduce((total, item) => total + (known[item.itemId]?.kind === 'equipment' ? item.count : 1), 0);
/** Name initial for icon-less tiles, skipping legacy / heritage prefixes. */
export const nameInitial = (name: string) => (name.replace(/^(（旧）|\(旧\)|传承：|传承:)/, '').trim() || name).slice(0, 1);
