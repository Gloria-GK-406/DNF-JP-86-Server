import type { PlayerPage } from './api';
export type MailEquipmentOptions = { seal: 'default' | 'unsealed'; growth: 'none' | 'reinforcement' | 'amplification'; level: number; amplifyType: number };
export type MailItem = { itemId: number; count: number; equipment?: MailEquipmentOptions | null };
export type MailCatalogEntry = { itemId: number; name: string; category: string; equipmentType: string | null; level: number | null; rarity: number | null };
export type MailCatalogPage = { items: MailCatalogEntry[]; page: number; pageSize: number; hasMore: boolean };
export type MailEquipmentCapabilities = { maxReinforcementLevel: number; maxAmplificationLevel: number; canAmplify: boolean;
  reinforcementReason: string | null; amplificationReason: string | null; amplifyValues: Record<string, number> };
export type MailItemDetails = { item: MailCatalogEntry; kind: string; defaultSealed: boolean; durability: number; equipment: MailEquipmentCapabilities | null };
export type MailInput = { title: string; body: string; recipientIds: number[]; items: MailItem[]; gold: number };
export type MailDocument = { id: string; name: string; revision: number; input: MailInput };
export type MailRecipient = { characterId: number; name: string; nameDecodeStatus: string; nameBytesHex: string | null; level: number; job: number; growType: number };
export type MailPreview = { input: MailInput; displayText: string; utf8Bytes: number; recipients: MailRecipient[];
  items: { itemId: number; count: number; kind: string; durability: number; sealed: boolean; name?: string; upgradeLevel?: number; amplifyType?: number; amplifyValue?: number }[]; totalGold: number; totalItems: number };
export type MailReceipt = { requestId: string; administratorId: number; administratorName: string; committedAt: string; expiresAt: string; input: MailInput;
  recipients: { character: MailRecipient; accountId: number; mailId: number; equipmentUids: number[] }[]; items?: MailPreview['items'] | null };
export type MailSendResult = { receipt: MailReceipt; replayed: boolean; reminder: string };
export type PendingMail = { requestId: string; input: MailInput };
export type MailHistory = PlayerPage<MailReceipt>;
export const emptyMail = (): MailInput => ({ title: '', body: '', recipientIds: [], items: [], gold: 0 });
export const defaultEquipment = (): MailEquipmentOptions => ({ seal: 'default', growth: 'none', level: 0, amplifyType: 0 });
export const itemCategories: Record<string, string> = { equipment: '装备', material: '材料', consumable: '消耗品', quest: '任务物品', other: '其他' };
export const equipmentTypes: Record<string, string> = { weapon: '武器', coat: '上衣', shoulder: '头肩', pants: '下装', shoes: '鞋', waist: '腰带',
  amulet: '项链', wrist: '手镯', ring: '戒指', support: '辅助装备', 'magic stone': '魔法石', 'title name': '称号' };
export const itemRarities = ['普通', '高级', '稀有', '神器', '史诗', '勇者', '传说'];
export const amplifyTypes: Record<number, string> = { 3: '力量', 4: '智力', 1: '体力', 2: '精神' };
export function equipmentSummary(options?: MailEquipmentOptions | null) {
  if (!options) return '模板默认状态';
  const growth = options.growth === 'reinforcement' ? `强化 +${options.level}`
    : options.growth === 'amplification' ? `增幅 +${options.level} · ${amplifyTypes[options.amplifyType] ?? '未知属性'}` : '无强化 / 增幅';
  return `${options.seal === 'unsealed' ? '解除封装' : '模板默认封装'} · ${growth}`;
}
export function mergeMailItem(items: MailItem[], item: MailItem, editingIndex?: number): MailItem[] {
  const source = editingIndex === undefined ? [...items, item] : items.map((old, index) => index === editingIndex ? item : old);
  const merged: MailItem[] = []; const positions = new Map<string, number>();
  for (const row of source) {
    const equipment = row.equipment ?? defaultEquipment();
    const key = JSON.stringify([row.itemId, equipment.seal, equipment.growth, equipment.level, equipment.amplifyType]);
    const previous = positions.get(key);
    if (previous === undefined) { positions.set(key, merged.length); merged.push({ ...row }); }
    else {
      const count = merged[previous].count + row.count;
      if (!Number.isSafeInteger(count) || count > 2147483647) throw new Error('合并后的数量超出允许范围。');
      merged[previous] = { ...merged[previous], count };
    }
  }
  return merged;
}
export function readPending(key: string): PendingMail | null {
  try {
    const item = JSON.parse(sessionStorage.getItem(key) ?? 'null') as PendingMail | null;
    return item && typeof item.requestId === 'string' && item.input && typeof item.input.body === 'string'
      && typeof item.input.title === 'string' && Array.isArray(item.input.items) && Array.isArray(item.input.recipientIds) ? item : null;
  } catch { return null; }
}
