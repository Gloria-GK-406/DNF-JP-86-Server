import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { api, ApiError } from './api';
import type { InitializationViewData } from './initialization';
import { renderApp } from './test-utils';

vi.mock('./api', async original => ({ ...await original<typeof import('./api')>(), api: vi.fn() }));
const mocked = vi.mocked(api);
const identity = { id: 1, loginName: '配置管理员', permissions: ['QueryPlayers', 'SendMail', 'GrantCera', 'ViewAudit'], expiresAt: '2030-01-01T00:00:00Z' };
let data: InitializationViewData;
const open = () => renderApp('/initialization');
const panel = (name: '账号初始化' | '角色初始化') => screen.getByRole('region', { name });
async function edit(kind: '账号' | '角色') {
  const button = await screen.findByRole('button', { name: new RegExp(`编辑${kind}配置`) });
  fireEvent.click(button);
}
async function save() {
  fireEvent.click(screen.getByRole('button', { name: '保存并生效' }));
  fireEvent.click(await screen.findByRole('button', { name: /^保\s*存$/ }));
}
const writes = (kind: string) => mocked.mock.calls.filter(([p]) => p === `initialization/${kind}`);
beforeEach(() => {
  data = { account: { revision: 2, input: { cera: 100, tokenCera: 20, happyTokenCera: 0, accountCargoLevel: 0, contracts: [{ type: 97, days: 30 }] }, updatedAt: null, updatedBy: '配置管理员' },
    character: { revision: 3, input: { level: 1, personalCargoLevel: 8, items: [] }, updatedAt: null, updatedBy: '配置管理员' },
    options: { maximumLevel: 86, contracts: [{ value: 97, label: '晶之契约' }], personalCargo: [{ value: 8, label: '破烂的铁质金库（8格）' }, { value: 24, label: '生锈的铜质金库（24格）' }], accountCargo: [{ value: 0, label: '未开通' }, { value: 8, label: '账号金库（8格）' }] } };
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'auth/status') return { initialized: true }; if (path === 'auth/session') return identity;
    if (path === 'initialization') return structuredClone(data);
    if (path === 'initialization/account' || path === 'initialization/character') {
      const kind = path.endsWith('account') ? 'account' : 'character';
      data[kind] = { ...data[kind], revision: data[kind].revision + 1, input: (payload as { input: never }).input };
      return data[kind];
    }
    if (path.startsWith('initialization/items?')) return { items: [{ itemId: 3, name: '测试消耗品', category: 'consumable', equipmentType: null, level: 1, rarity: 1 }], page: 1, pageSize: 30, hasMore: false };
    if (path === 'initialization/items/3') return { item: { itemId: 3, name: '测试消耗品', category: 'consumable', equipmentType: null, level: 1, rarity: 1 }, kind: 'stackable', defaultSealed: false, durability: 0, equipment: null };
    if (path === 'server-info' || path === 'items/icon-pack' || path === 'overview') return undefined;
    throw new Error('Unexpected request: ' + path);
  });
});
it('账号与角色并排展示生效摘要，编辑才出现表单', async () => {
  open(); await screen.findByText('30 天');
  expect(within(panel('角色初始化')).getByText('Lv.1')).toBeTruthy();
  expect(screen.queryByLabelText('初始角色等级')).toBeNull();
  await edit('角色'); expect(await screen.findByLabelText('初始角色等级')).toBeTruthy();
});
it('账号独立保存全部字段及原版本，保存前展示改动对比', async () => {
  open(); await edit('账号');
  fireEvent.change(await screen.findByLabelText('初始点券'), { target: { value: '250' } });
  expect(screen.getByText('保存后会变成：')).toBeTruthy(); expect(screen.getByText('改了 1 项')).toBeTruthy();
  await save();
  await screen.findByText('初始化配置已保存并生效，仅影响后续创建。');
  expect(mocked).toHaveBeenCalledWith('initialization/account', { expectedRevision: 2, input: { cera: 250, tokenCera: 20, happyTokenCera: 0, accountCargoLevel: 0, contracts: [{ type: 97, days: 30 }] } });
  await waitFor(() => expect(screen.queryByLabelText('初始点券')).toBeNull());
  expect(writes('character')).toHaveLength(0);
});
it('未修改时不能保存', async () => {
  open(); await edit('账号');
  expect((screen.getByRole('button', { name: '保存并生效' }) as HTMLButtonElement).disabled).toBe(true);
  fireEvent.click(within(panel('账号初始化')).getByRole('button', { name: /取\s*消/ }));
  await waitFor(() => expect(screen.queryByLabelText('初始点券')).toBeNull());
  expect(writes('account')).toHaveLength(0);
});
it('角色物品通过初始化专用目录选择，保存包含等级与物品', async () => {
  open(); await edit('角色');
  fireEvent.change(await screen.findByLabelText('初始角色等级'), { target: { value: '30' } });
  fireEvent.click(screen.getByRole('button', { name: /添加初始物品/ }));
  const picker = await screen.findByRole('dialog');
  fireEvent.click(await within(picker).findByRole('button', { name: '添加物品 3' }));
  expect((within(picker).getByRole('button', { name: '添加物品 3' }) as HTMLButtonElement).disabled).toBe(true);
  fireEvent.click(await within(picker).findByRole('button', { name: '加入初始物品（1 项）' }));
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  expect(await screen.findByText('测试消耗品')).toBeTruthy();
  await save();
  await screen.findByText('初始化配置已保存并生效，仅影响后续创建。');
  expect(mocked).toHaveBeenCalledWith('initialization/character', { expectedRevision: 3, input: { level: 30, personalCargoLevel: 8, items: [{ itemId: 3, count: 1 }] } });
  expect(mocked.mock.calls.some(([p]) => p.startsWith('mail/'))).toBe(false);
});
it('版本冲突锁定写入，重新读取后才能再编辑', async () => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => { if (path === 'initialization/account') throw new ApiError(409, '配置已被其他管理员修改，请重新读取后编辑。'); return original(path, payload); });
  open(); await edit('账号');
  fireEvent.change(await screen.findByLabelText('初始点券'), { target: { value: '250' } }); await save();
  expect(await screen.findByText('配置已被其他管理员修改，请重新读取后编辑。')).toBeTruthy();
  expect(await screen.findByText('请先重新读取生效配置')).toBeTruthy();
  expect((screen.getByRole('button', { name: '保存并生效' }) as HTMLButtonElement).disabled).toBe(true);
  fireEvent.click(screen.getAllByRole('button', { name: /重新读取生效配置/ }).at(-1)!);
  await waitFor(() => expect(screen.queryByText('请先重新读取生效配置')).toBeNull());
  expect(screen.queryByLabelText('初始点券')).toBeNull(); expect(writes('account')).toHaveLength(1);
  await waitFor(() => expect((screen.getByRole('button', { name: /编辑账号配置/ }) as HTMLButtonElement).disabled).toBe(false));
});
it('结果未知时先读取生效配置，不能重复提交旧编辑', async () => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => { if (path === 'initialization/account') throw new Error('网络超时'); return original(path, payload); });
  open(); await edit('账号');
  fireEvent.change(await screen.findByLabelText('初始点券'), { target: { value: '250' } }); await save();
  expect(await screen.findByText('请先重新读取生效配置')).toBeTruthy();
  expect((screen.getByRole('button', { name: '保存并生效' }) as HTMLButtonElement).disabled).toBe(true);
  expect((screen.getByRole('button', { name: /编辑角色配置/ }) as HTMLButtonElement).disabled).toBe(true);
});
it('部分权限不显示入口，直接访问也不会读取配置', async () => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => path === 'auth/session' ? { ...identity, permissions: ['GrantCera'] } : original(path, payload));
  open(); await screen.findByText('没有访问此页面的权限');
  expect(within(screen.getByRole('navigation', { name: '主导航' })).queryByText('初始化配置')).toBeNull();
  expect(mocked.mock.calls.some(([p]) => p === 'initialization')).toBe(false);
});
