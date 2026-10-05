import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import { AdminApp } from './AdminApp';
import { api, ApiError } from './api';
import type { InitializationViewData } from './initialization';

vi.mock('./api', async original => ({ ...await original<typeof import('./api')>(), api: vi.fn() }));
const mocked = vi.mocked(api);
const identity = { id: 1, loginName: '配置管理员', permissions: ['QueryPlayers', 'SendMail', 'GrantCera', 'ViewAudit'], expiresAt: '2030-01-01T00:00:00Z' };
let data: InitializationViewData;
function open() { return render(<ConfigProvider theme={{ token: { motion: false } }}><App><MemoryRouter initialEntries={['/initialization']}><AdminApp /></MemoryRouter></App></ConfigProvider>); }
beforeEach(() => {
  data = { account: { revision: 2, input: { cera: 100, tokenCera: 20, happyTokenCera: 0, accountCargoLevel: 0, contracts: [{ type: 97, days: 30 }] }, updatedAt: null, updatedBy: '配置管理员' },
    character: { revision: 3, input: { level: 1, personalCargoLevel: 8, items: [] }, updatedAt: null, updatedBy: '配置管理员' },
    options: { maximumLevel: 86, contracts: [{ value: 97, label: '晶之契约' }], personalCargo: [{ value: 8, label: '8 格' }, { value: 24, label: '24 格' }], accountCargo: [{ value: 0, label: '未开通' }, { value: 8, label: '8 格' }] } };
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'auth/status') return { initialized: true }; if (path === 'auth/session') return identity;
    if (path === 'initialization') return structuredClone(data);
    if (path === 'initialization/account' || path === 'initialization/character') {
      const kind = path.endsWith('account') ? 'account' : 'character';
      data[kind] = { ...data[kind], revision: data[kind].revision + 1, input: (payload as { input: never }).input };
      return data[kind];
    }
    if (path.startsWith('initialization/items?')) return { items: [{ itemId: 3, name: '测试消耗品', category: 'consumable', level: 1, rarity: 1 }], hasMore: false };
    if (path === 'initialization/items/3') return { item: { itemId: 3, name: '测试消耗品', category: 'consumable', level: 1, rarity: 1 }, kind: 'stackable', equipment: null };
    throw new Error('Unexpected request: ' + path);
  });
});

it('单一导航下分两个Tab，默认展示生效摘要，编辑才打开窗口', async () => {
  open(); await screen.findByText('当前账号初始化配置');
  expect(screen.queryByRole('dialog')).toBeNull();
  expect(screen.getByText('30 天')).toBeTruthy();
  fireEvent.click(screen.getByRole('tab', { name: '角色初始化' }));
  expect(await screen.findByText('当前角色初始化配置')).toBeTruthy();
  expect(screen.queryByLabelText('初始角色等级')).toBeNull();
  fireEvent.click(screen.getByRole('button', { name: '编辑角色配置' }));
  expect(await screen.findByRole('dialog', { name: '编辑角色初始化配置' })).toBeTruthy();
});

it('账号独立保存全部字段及原版本，成功刷新生效摘要', async () => {
  open(); fireEvent.click(await screen.findByRole('button', { name: '编辑账号配置' }));
  fireEvent.change(await screen.findByLabelText('初始点券'), { target: { value: '250' } });
  fireEvent.click(screen.getByRole('button', { name: '保存并生效' }));
  await screen.findByText('初始化配置已保存并生效，仅影响后续创建。');
  expect(mocked).toHaveBeenCalledWith('initialization/account', { expectedRevision: 2, input: { cera: 250, tokenCera: 20, happyTokenCera: 0, accountCargoLevel: 0, contracts: [{ type: 97, days: 30 }] } });
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  expect(mocked.mock.calls.some(([p]) => p === 'initialization/character')).toBe(false);
});

it('角色物品通过独立权限目录选择，保存包含等级与物品', async () => {
  open(); await screen.findByText('当前账号初始化配置'); fireEvent.click(screen.getByRole('tab', { name: '角色初始化' }));
  fireEvent.click(await screen.findByRole('button', { name: '编辑角色配置' }));
  fireEvent.change(await screen.findByLabelText('初始角色等级'), { target: { value: '30' } });
  fireEvent.click(screen.getByRole('button', { name: '添加初始物品' }));
  await waitFor(() => expect(mocked.mock.calls.some(([p]) => p.startsWith('initialization/items?'))).toBe(true));
  // rc-dialog uses one fixed title ID in the test environment for nested modals.
  const picker = (await screen.findByText('添加初始物品', { selector: '.ant-modal-title' })).closest('[role=dialog]') as HTMLElement;
  fireEvent.click(await within(picker).findByLabelText('选择物品 3'));
  fireEvent.click(await within(picker).findByRole('button', { name: '加入初始物品' }));
  await waitFor(() => expect(screen.queryByText('添加初始物品', { selector: '.ant-modal-title' })).toBeNull());
  fireEvent.click(screen.getByRole('button', { name: '保存并生效' }));
  await screen.findByText('初始化配置已保存并生效，仅影响后续创建。');
  expect(mocked).toHaveBeenCalledWith('initialization/character', { expectedRevision: 3, input: { level: 30, personalCargoLevel: 8, items: [{ itemId: 3, count: 1 }] } });
  expect(mocked.mock.calls.some(([p]) => p.startsWith('mail/'))).toBe(false);
});

it('版本冲突阻止盲目重试，并提供重新读取', async () => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => { if (path === 'initialization/account') throw new ApiError(409, '配置已被其他管理员修改，请重新读取后编辑。'); return original(path, payload); });
  open(); fireEvent.click(await screen.findByRole('button', { name: '编辑账号配置' }));
  fireEvent.click(await screen.findByRole('button', { name: '保存并生效' }));
  expect(await screen.findByText('配置已被其他管理员修改，请重新读取后编辑。')).toBeTruthy();
  expect(screen.queryByRole('button', { name: '保存并生效' })).toBeNull();
  fireEvent.click(screen.getByRole('button', { name: '重新读取生效配置' }));
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  expect(mocked.mock.calls.filter(([p]) => p === 'initialization/account')).toHaveLength(1);
});

it('结果未知时先读取生效配置，不能重复提交旧编辑', async () => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => { if (path === 'initialization/account') throw new Error('网络超时'); return original(path, payload); });
  open(); fireEvent.click(await screen.findByRole('button', { name: '编辑账号配置' }));
  fireEvent.click(await screen.findByRole('button', { name: '保存并生效' }));
  expect(await screen.findByRole('button', { name: '重新读取生效配置' })).toBeTruthy();
  expect(screen.queryByRole('button', { name: '保存并生效' })).toBeNull();
});

it('部分权限不显示入口，直接访问也不会读取配置', async () => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => path === 'auth/session' ? { ...identity, permissions: ['GrantCera'] } : original(path, payload));
  open(); await screen.findByText('当前页面不可访问');
  expect(screen.queryByRole('menuitem', { name: '初始化配置' })).toBeNull();
  expect(mocked.mock.calls.some(([p]) => p === 'initialization')).toBe(false);
});

it('未修改直接取消不保存', async () => {
  open(); fireEvent.click(await screen.findByRole('button', { name: '编辑账号配置' }));
  const editor = await screen.findByRole('dialog'); fireEvent.click(within(editor).getByRole('button', { name: /取\s*消/ }));
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  expect(mocked.mock.calls.some(([p]) => p === 'initialization/account')).toBe(false);
});
