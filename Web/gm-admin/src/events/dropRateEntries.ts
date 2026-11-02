export type DrawMultipliers = Record<string, number>;

/** The server's ground-drop entries (`DropDrawEntries`), grouped for the activity editor. */
export const dropEntryGroups: { title: string; entries: { key: string; label: string }[] }[] = [
  { title: '怪物', entries: [{ key: 'monster.gold', label: '金币' }, { key: 'monster.stackable', label: '道具' }, { key: 'monster.equipment', label: '装备' },
    { key: 'monster.extra', label: 'Extra' }, { key: 'monster.declared', label: '怪物指定' }] },
  { title: '独立与世界', entries: [{ key: 'independent.monster', label: '独立怪物' }, { key: 'independent.apc', label: '独立APC' }, { key: 'world', label: '世界' }] },
  { title: '物件', entries: [{ key: 'object.random.gold', label: '随机金币' }, { key: 'object.random.item', label: '随机道具' }, { key: 'object.specific', label: '指定掉落' }] },
  { title: '深渊', entries: [{ key: 'hell.equipment', label: '装备' }, { key: 'hell.epic_piece', label: '史诗碎片' }] },
  { title: '异界', entries: [{ key: 'dimension.equipment', label: '装备' }, { key: 'dimension.material', label: '材料' }] },
  { title: '遗忘之地', entries: [{ key: 'lostland.regular', label: '普通' }, { key: 'lostland.boss', label: 'Boss' }] },
];
export const dropEntryKeys = dropEntryGroups.flatMap(group => group.entries.map(entry => entry.key));
