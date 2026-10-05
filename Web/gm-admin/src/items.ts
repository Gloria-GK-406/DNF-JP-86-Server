export type ItemEquipmentOptions = { seal: 'default' | 'unsealed'; growth: 'none' | 'reinforcement' | 'amplification'; level: number; amplifyType: number };
export type ItemInput = { itemId: number; count: number; equipment?: ItemEquipmentOptions | null };
export type ItemCatalogEntry = { itemId: number; name: string; category: string; equipmentType: string | null; level: number | null; rarity: number | null };
export type ItemCatalogPage = { items: ItemCatalogEntry[]; page: number; pageSize: number; hasMore: boolean };
export type ItemEquipmentCapabilities = { maxReinforcementLevel: number; maxAmplificationLevel: number; canAmplify: boolean;
  reinforcementReason: string | null; amplificationReason: string | null; amplifyValues: Record<string, number> };
export type ItemCatalogDetails = { item: ItemCatalogEntry; kind: string; defaultSealed: boolean; durability: number; equipment: ItemEquipmentCapabilities | null };
export const defaultEquipment = (): ItemEquipmentOptions => ({ seal: 'default', growth: 'none', level: 0, amplifyType: 0 });
export const itemCategories: Record<string, string> = { equipment: '装备', material: '材料', consumable: '消耗品', quest: '任务物品', other: '其他' };
export const equipmentTypes: Record<string, string> = { weapon: '武器', coat: '上衣', shoulder: '头肩', pants: '下装', shoes: '鞋', waist: '腰带',
  amulet: '项链', wrist: '手镯', ring: '戒指', support: '辅助装备', 'magic stone': '魔法石', 'title name': '称号' };
export const itemRarities = ['普通', '高级', '稀有', '神器', '史诗', '勇者', '传说'];
export const amplifyTypes: Record<number, string> = { 3: '力量', 4: '智力', 1: '体力', 2: '精神' };
export function equipmentSummary(options?: ItemEquipmentOptions | null) {
  if (!options) return '模板默认状态';
  const growth = options.growth === 'reinforcement' ? `强化 +${options.level}`
    : options.growth === 'amplification' ? `增幅 +${options.level} · ${amplifyTypes[options.amplifyType] ?? '未知属性'}` : '无强化 / 增幅';
  return `${options.seal === 'unsealed' ? '解除封装' : '模板默认封装'} · ${growth}`;
}
