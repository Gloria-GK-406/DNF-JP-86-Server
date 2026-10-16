import { expect, it } from 'vitest';
import { careerName, mailDisplayText, utf8Bytes } from './format';
import { equipKey, equipOptionsOf, equipStateOf, mergeItems, nameInitial, usedSlots, type ItemCatalogDetails } from './items';

it('装备默认状态（最上级、无成长）不产生配置；非默认字段才写入', () => {
  expect(equipOptionsOf(equipStateOf(null))).toBeNull();
  expect(equipOptionsOf({ ...equipStateOf(null), quality: 4, forge: 0, socket: false })).toBeNull();
  expect(equipOptionsOf({ ...equipStateOf(null), quality: 2 })).toEqual({ seal: 'default', growth: 'none', level: 0, amplifyType: 0, quality: 2 });
  expect(equipOptionsOf({ ...equipStateOf(null), growth: 'none', level: 7, amplifyType: 3 })).toBeNull();
  expect(equipKey({ seal: 'default', growth: 'none', level: 0, amplifyType: 0, quality: 4 })).toBe('');
});
it('合并按物品与装备状态，计数溢出时拒绝', () => {
  const merged = mergeItems([{ itemId: 1, count: 2 }], [{ itemId: 1, count: 3 }, { itemId: 1, count: 1, equipment: { seal: 'unsealed', growth: 'none', level: 0, amplifyType: 0 } }]);
  expect(merged).toEqual([{ itemId: 1, count: 5 }, { itemId: 1, count: 1, equipment: { seal: 'unsealed', growth: 'none', level: 0, amplifyType: 0 } }]);
  expect(() => mergeItems([{ itemId: 1, count: 2147483647 }], [{ itemId: 1, count: 1 }])).toThrow('合并后的数量超出允许范围。');
  const known = { 1: { kind: 'equipment' } as ItemCatalogDetails, 2: { kind: 'stackable' } as ItemCatalogDetails };
  expect(usedSlots([{ itemId: 1, count: 3 }, { itemId: 2, count: 999 }], known)).toBe(4);
});
it('游戏邮件文字与服务端一致：空正文用默认文案，标题等于正文只显示一次', () => {
  expect(mailDisplayText('', '')).toBe('此为官方邮件，无需回复。');
  expect(mailDisplayText('补偿', ' ')).toBe('补偿\n此为官方邮件，无需回复。');
  expect(mailDisplayText('同样', '同样')).toBe('同样');
  expect(utf8Bytes('补偿\nab')).toBe(9);
});
it('职业与转职名称取自 PVF，觉醒位在高四位', () => {
  expect(careerName(0, 1)).toBe('剑魂'); expect(careerName(3, 0)).toBe('魔法师（女）');
  expect(careerName(6, 0x12)).toBe('死灵术士（觉醒）'); expect(careerName(99, 0)).toBe('职业 99');
  expect(nameInitial('（旧）黄铜长靴')).toBe('黄'); expect(nameInitial('传承：摩羯天使薄纱长袍')).toBe('摩');
});
