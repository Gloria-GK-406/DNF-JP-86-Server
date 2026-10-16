import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { api, ApiError } from './api';
import type { ItemCatalogDetails, ItemCatalogEntry } from './items';
import { renderApp } from './test-utils';
vi.mock('./api', async original => ({ ...await original<typeof import('./api')>(), api: vi.fn() }));
const mocked = vi.mocked(api);
const identity = { id: 1, loginName: '邮件管理员', permissions: ['SendMail'], expiresAt: '2030-01-01T00:00:00Z' };
const empty = { title: '', body: '', recipientIds: [], items: [], gold: 0 };
const material: ItemCatalogEntry = { itemId: 3037, name: '无色小晶块', category: 'material', equipmentType: null, level: null, rarity: 1 };
const weapon: ItemCatalogEntry = { itemId: 27650, name: '复仇灵魂之刀', category: 'equipment', equipmentType: 'weapon', level: 20, rarity: 2 };
const coat: ItemCatalogEntry = { itemId: 100330668, name: '传承：摩羯天使薄纱长袍', category: 'equipment', equipmentType: 'coat', level: 70, rarity: 2 };
const caps = { maxReinforcementLevel: 20, maxAmplificationLevel: 0, canAmplify: false, reinforcementReason: null, amplificationReason: '该装备的等级、稀有度或模板规则不支持增幅。', amplifyValues: {} };
const details: Record<number, ItemCatalogDetails> = {
  3037: { item: material, kind: 'stackable', defaultSealed: false, durability: 0, equipment: null },
  27650: { item: weapon, kind: 'equipment', defaultSealed: true, durability: 45, equipment: { ...caps, maxForgeLevel: 8, socketCount: 0, socketClass: null } },
  100330668: { item: coat, kind: 'equipment', defaultSealed: false, durability: 28, equipment: { ...caps, maxAmplificationLevel: 20, canAmplify: true, amplificationReason: null,
    amplifyValues: { 1: 10, 2: 10, 3: 10, 4: 10 }, maxForgeLevel: 0, socketCount: 2, socketClass: 'C' } },
};
let catalog: ItemCatalogEntry[];
let draft = { id: '', name: '草稿', revision: 0, input: empty as Record<string, unknown> };
beforeEach(() => {
  catalog = [material, weapon, coat]; draft = { id: '', name: '草稿', revision: 0, input: empty };
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'auth/status') return { initialized: true };
    if (path === 'auth/session') return identity;
    if (path === 'mail/draft') return draft;
    if (path === 'mail/templates') return [];
    if (path.startsWith('mail/operations?')) return { items: [], total: 0, page: 1, pageSize: 20 };
    if (path.startsWith('mail/items?')) return { items: catalog, page: 1, pageSize: 30, hasMore: false };
    const match = /^mail\/items\/(\d+)$/.exec(path); if (match) return details[+match[1]];
    if (path === 'server-info' || path === 'items/icon-pack' || path === 'overview') return undefined;
    throw new Error('Unexpected path ' + path + JSON.stringify(payload));
  });
});
async function composeWith(items: unknown[] = []) {
  draft = { ...draft, revision: 1, input: { ...empty, title: '附件测试', items } };
  renderApp('/grants'); fireEvent.click(await screen.findByRole('button', { name: '接着写' })); await screen.findByLabelText('邮件正文');
}
async function picker() { fireEvent.click(screen.getByRole('button', { name: /添加物品/ })); return screen.findByRole('dialog'); }
const confirmButton = (dialog: HTMLElement) => within(dialog).getByRole('button', { name: /^加入邮件/ }) as HTMLButtonElement;
async function savedDraft() {
  fireEvent.click(screen.getByRole('button', { name: /保存草稿/ })); await screen.findByText('草稿已保存。');
  return (mocked.mock.calls.filter(([path, payload]) => path === 'mail/draft' && payload).at(-1)![1] as { items: unknown[] }).items;
}
beforeEach(() => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload, method) => path === 'mail/draft' && payload ? { ...draft, revision: draft.revision + 1, input: payload } : original(path, payload, method));
});

it('连续配置多张卡片，统一加入：堆叠物只带数量，装备带品级、强化与锻造', async () => {
  await composeWith(); const dialog = await picker();
  fireEvent.click(await within(dialog).findByRole('button', { name: '添加物品 3037' }));
  fireEvent.click(within(dialog).getByRole('button', { name: '添加物品 27650' }));
  fireEvent.change(await within(dialog).findByLabelText('数量 1'), { target: { value: '200' } });
  const card = await within(dialog).findByLabelText('待加入 2');
  await within(card).findByText('品级');
  fireEvent.click(within(card).getByText('上级'));
  expect(within(card).getByText(/属性取该档上限 80%/)).toBeTruthy();
  fireEvent.click(within(card).getByText('强化'));
  fireEvent.change(within(card).getByLabelText('等级'), { target: { value: '10' } });
  fireEvent.change(within(card).getByLabelText('锻造'), { target: { value: '5' } });
  expect(within(card).getByText(/保留封装会生成/)).toBeTruthy();
  expect(within(card).getByText('武器没有徽章孔')).toBeTruthy();
  expect(within(dialog).getByText(/加入后每人占用/).textContent).toContain('2');
  fireEvent.click(confirmButton(dialog));
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  expect(screen.getByText(/上级 · \+10 强化 · 锻造 \+5/)).toBeTruthy();
  expect(await savedDraft()).toEqual([{ itemId: 3037, count: 200 }, { itemId: 27650, count: 1, equipment: { seal: 'default', growth: 'reinforcement', level: 10, amplifyType: 0, forge: 5, quality: 3 } }]);
});
it('默认最上级的装备不带配置字段；开孔只在有徽章孔的部位出现', async () => {
  await composeWith(); const dialog = await picker();
  fireEvent.click(await within(dialog).findByRole('button', { name: '添加物品 100330668' }));
  fireEvent.click(within(dialog).getByRole('button', { name: '添加物品 100330668' }));
  const second = await within(dialog).findByLabelText('待加入 2');
  fireEvent.click(await within(second).findByRole('checkbox', { name: /开 2 个 C 类孔/ }));
  expect(within(second).getByText('只有武器能锻造')).toBeTruthy();
  fireEvent.click(within(second).getByText('增幅'));
  fireEvent.click(within(second).getByText('智力'));
  fireEvent.click(confirmButton(dialog)); await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  expect(await savedDraft()).toEqual([{ itemId: 100330668, count: 1 }, { itemId: 100330668, count: 1, equipment: { seal: 'default', growth: 'amplification', level: 0, amplifyType: 4, socket: true } }]);
});
it('相同物品与配置合并数量，不同配置分开放', async () => {
  await composeWith([{ itemId: 3037, count: 5 }, { itemId: 27650, count: 1, equipment: { seal: 'unsealed', growth: 'none', level: 0, amplifyType: 0 } }]);
  const dialog = await picker();
  fireEvent.click(await within(dialog).findByRole('button', { name: '添加物品 3037' }));
  fireEvent.click(within(dialog).getByRole('button', { name: '添加物品 27650' }));
  const card = await within(dialog).findByLabelText('待加入 2'); await within(card).findByText('品级');
  fireEvent.click(within(card).getByRole('checkbox', { name: /解除封装/ }));
  fireEvent.click(within(dialog).getByRole('button', { name: '添加物品 27650' }));
  fireEvent.click(confirmButton(dialog)); await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  expect(await savedDraft()).toEqual([{ itemId: 3037, count: 6 }, { itemId: 27650, count: 2, equipment: { seal: 'unsealed', growth: 'none', level: 0, amplifyType: 0 } }, { itemId: 27650, count: 1 }]);
});
it('计入已有附件容量，超限不部分加入；满位仍能合并已有堆叠物', async () => {
  await composeWith([{ itemId: 27650, count: 9 }]); const dialog = await picker();
  fireEvent.click(await within(dialog).findByRole('button', { name: '添加物品 27650' }));
  fireEvent.click(within(dialog).getByRole('button', { name: '添加物品 3037' }));
  await within(dialog).findByLabelText('数量 2');
  await waitFor(() => expect(within(dialog).getByRole('alert').textContent).toContain('超过 10 格了'));
  expect(confirmButton(dialog).disabled).toBe(true);
  fireEvent.click(within(dialog).getByRole('button', { name: '移除待加入 1' }));
  await waitFor(() => expect(confirmButton(dialog).disabled).toBe(false));
  fireEvent.click(confirmButton(dialog)); await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  expect(screen.getByText('10/10 格')).toBeTruthy();
  const again = await picker(); fireEvent.click(await within(again).findByRole('button', { name: '添加物品 3037' }));
  await within(again).findByLabelText('数量 1'); await waitFor(() => expect(confirmButton(again).disabled).toBe(false));
  fireEvent.click(confirmButton(again)); await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  expect(await savedDraft()).toEqual([{ itemId: 27650, count: 9 }, { itemId: 3037, count: 2 }]);
});
it('数量为空或规则未读到时阻止加入；单张读取失败可重试且不丢其他配置', async () => {
  let failures = 1; const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload, method) => { if (path === 'mail/items/27650' && failures-- > 0) throw new ApiError(500, '规则读取超时'); return original(path, payload, method); });
  await composeWith(); const dialog = await picker();
  fireEvent.click(await within(dialog).findByRole('button', { name: '添加物品 3037' }));
  fireEvent.change(await within(dialog).findByLabelText('数量 1'), { target: { value: '' } });
  await waitFor(() => expect(within(dialog).getByText('数量至少 1')).toBeTruthy()); expect(confirmButton(dialog).disabled).toBe(true);
  fireEvent.change(within(dialog).getByLabelText('数量 1'), { target: { value: '7' } });
  fireEvent.click(within(dialog).getByRole('button', { name: '添加物品 27650' }));
  fireEvent.click(await within(dialog).findByRole('button', { name: '重试读取规则' }));
  await within(await within(dialog).findByLabelText('待加入 2')).findByText('品级');
  expect((within(dialog).getByLabelText('数量 1') as HTMLInputElement).value).toBe('7');
  await waitFor(() => expect(confirmButton(dialog).disabled).toBe(false));
});
it('移除卡片不串值，取消丢弃本次选择且不改已有附件', async () => {
  await composeWith([{ itemId: 3037, count: 3 }]); const dialog = await picker();
  fireEvent.click(await within(dialog).findByRole('button', { name: '添加物品 3037' }));
  fireEvent.click(within(dialog).getByRole('button', { name: '添加物品 3037' }));
  fireEvent.change(await within(dialog).findByLabelText('数量 1'), { target: { value: '11' } });
  fireEvent.change(within(dialog).getByLabelText('数量 2'), { target: { value: '22' } });
  fireEvent.click(within(dialog).getByRole('button', { name: '移除待加入 1' }));
  expect((within(dialog).getByLabelText('数量 1') as HTMLInputElement).value).toBe('22');
  fireEvent.click(within(dialog).getByRole('button', { name: /取\s*消/ }));
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  expect((screen.getByLabelText('附件 1 数量') as HTMLInputElement).value).toBe('3');
});
it('搜索：数字按物品 ID 查，分类切换回到第一页，装备分类才出现部位与等级', async () => {
  await composeWith(); const dialog = await picker(); await within(dialog).findByRole('button', { name: '添加物品 3037' });
  fireEvent.change(within(dialog).getByLabelText('物品搜索'), { target: { value: '27650' } });
  fireEvent.keyDown(within(dialog).getByLabelText('物品搜索'), { key: 'Enter', code: 'Enter', keyCode: 13 });
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('mail/items?page=1&pageSize=30&itemId=27650'));
  fireEvent.change(within(dialog).getByLabelText('物品搜索'), { target: { value: '晶块%' } });
  fireEvent.keyDown(within(dialog).getByLabelText('物品搜索'), { key: 'Enter', code: 'Enter', keyCode: 13 });
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('mail/items?page=1&pageSize=30&q=%E6%99%B6%E5%9D%97%25'));
  expect(within(dialog).queryByLabelText('装备部位')).toBeNull();
  fireEvent.click(within(dialog).getByText('装备'));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('mail/items?page=1&pageSize=30&q=%E6%99%B6%E5%9D%97%25&category=equipment'));
  expect(within(dialog).getByLabelText('装备部位')).toBeTruthy();
});
it('附件行的配置小窗修改装备状态，和同状态的行合并', async () => {
  await composeWith([{ itemId: 27650, count: 1 }, { itemId: 27650, count: 2, equipment: { seal: 'default', growth: 'none', level: 0, amplifyType: 0, quality: 0 } }]);
  fireEvent.click(await screen.findByRole('button', { name: /配置附件 1/ }));
  const modal = await screen.findByRole('dialog'); await within(modal).findByText('品级');
  fireEvent.click(within(modal).getByText('最下级')); fireEvent.click(within(modal).getByRole('button', { name: '保存配置' }));
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  expect(await savedDraft()).toEqual([{ itemId: 27650, count: 3, equipment: { seal: 'default', growth: 'none', level: 0, amplifyType: 0, quality: 0 } }]);
});
it.each([401, 403])('目录访问失败 %s 触发原有身份处理', async status => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload, method) => { if (path.startsWith('mail/items?')) throw new ApiError(status, '无权'); return original(path, payload, method); });
  await composeWith(); fireEvent.click(screen.getByRole('button', { name: /添加物品/ }));
  if (status === 401) await screen.findByLabelText('管理员账号'); else await screen.findByText('没有访问此页面的权限');
  expect(screen.queryByRole('dialog')).toBeNull();
});
