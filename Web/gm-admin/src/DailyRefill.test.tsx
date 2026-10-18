import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { Modal } from 'antd';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { api, ApiError } from './api';
import type { RefillList } from './DailyRefillView';
import { renderApp } from './test-utils';

vi.mock('./api', async original => ({ ...await original<typeof import('./api')>(), api: vi.fn() }));
const mocked = vi.mocked(api);
const identity = { id: 1, loginName: '补给管理员', permissions: ['QueryPlayers', 'SendMail', 'GrantCera', 'ViewAudit'], expiresAt: '2030-01-01T00:00:00Z' };
let data: RefillList;
afterEach(() => Modal.destroyAll());
const open = () => renderApp('/daily-refill');
const writes = () => mocked.mock.calls.filter(([p, payload]) => p === 'daily-refill' && payload);
beforeEach(() => {
  data = { revision: 8, entries: [
    { itemId: 3, name: '邀请函', source: 'pvf', quantity: 2, mode: 0, expiresAt: null, status: 'overridden', pvfEnabled: true },
    { itemId: 3, name: '邀请函', source: 'custom', quantity: 8, mode: 1, expiresAt: null, status: 'active', pvfEnabled: true },
    { itemId: 4, name: '旧补给', source: 'pvf', quantity: 0, mode: 0, expiresAt: '2000-01-01T00:00:00Z', status: 'expired', pvfEnabled: true },
  ], updatedAt: null, updatedBy: '补给管理员' };
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'auth/status') return { initialized: true }; if (path === 'auth/session') return identity;
    if (path === 'daily-refill') { if (payload) data.revision++; return structuredClone(data); }
    if (path.startsWith('daily-refill/items?')) return { items: [{ itemId: 7, name: '补给药水', category: 'consumable', equipmentType: null, level: 1, rarity: 1 }], page: 1, pageSize: 30, hasMore: false };
    if (path === 'daily-refill/items/7') return { item: { itemId: 7, name: '补给药水', category: 'consumable', equipmentType: null, level: 1, rarity: 1 }, kind: 'stackable', defaultSealed: false, durability: 0, equipment: null };
    if (path === 'server-info' || path === 'items/icon-pack' || path === 'overview') return undefined;
    throw new Error('Unexpected request: ' + path);
  });
});
it('默认展示 PVF 和自定义来源，包括覆盖与过期状态，可按来源筛选', async () => {
  open(); expect(await screen.findByText('已被自定义覆盖')).toBeTruthy();
  expect(screen.getByText('已过期')).toBeTruthy(); expect(screen.getByText('增量发放')).toBeTruthy(); expect(screen.getByText('+8')).toBeTruthy();
  expect(screen.queryByRole('dialog')).toBeNull();
  fireEvent.click(screen.getByText('自定义 1'));
  await waitFor(() => expect(screen.queryByText('已过期')).toBeNull()); expect(screen.getByText('+8')).toBeTruthy();
});
it('新增通过补给专用目录选择物品并保存数量、方式及原版本', async () => {
  open(); fireEvent.click(await screen.findByRole('button', { name: /添加补给/ }));
  fireEvent.click(await screen.findByRole('button', { name: '选择补给物品' }));
  const picker = (await screen.findByText('选择补给物品', { selector: '.ant-modal-title span' })).closest('[role=dialog]') as HTMLElement;
  fireEvent.click(await within(picker).findByRole('button', { name: '选择物品 7' }));
  fireEvent.click(await within(picker).findByRole('button', { name: '就用它' }));
  await waitFor(() => expect(screen.queryByText('选择补给物品', { selector: '.ant-modal-title span' })).toBeNull());
  expect(screen.getByText('补给药水')).toBeTruthy();
  fireEvent.change(screen.getByLabelText('目标数量'), { target: { value: '12' } });
  fireEvent.click(screen.getByRole('radio', { name: /增量发放/ }));
  fireEvent.click(screen.getByRole('button', { name: '保存并生效' }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('daily-refill', { expectedRevision: 8, action: 'add', itemId: 7, quantity: 12, mode: 1, expiresAt: null }));
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  expect(mocked.mock.calls.some(([p]) => p.startsWith('mail/') || p.startsWith('initialization/'))).toBe(false);
});
it('截止时间按北京时间提交，早于现在的拒绝保存', async () => {
  open(); fireEvent.click(await screen.findByRole('button', { name: /编\s*辑/ }));
  fireEvent.change(await screen.findByLabelText('截止时间（北京时间，留空为长期有效）'), { target: { value: '2001-01-01T08:00' } });
  expect(screen.getByText('截止时间要晚于现在；留空就是长期有效')).toBeTruthy();
  expect((screen.getByRole('button', { name: '保存并生效' }) as HTMLButtonElement).disabled).toBe(true);
  fireEvent.change(screen.getByLabelText('截止时间（北京时间，留空为长期有效）'), { target: { value: '2099-01-01T08:00' } });
  fireEvent.click(screen.getByRole('button', { name: '保存并生效' }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('daily-refill', expect.objectContaining({ action: 'update', itemId: 3, expiresAt: '2099-01-01T00:00:00.000Z' })));
});
it('删除自定义告知 PVF 恢复且保留当天记录，确认才发请求', async () => {
  open(); fireEvent.click(await screen.findByRole('button', { name: /删\s*除/ }));
  expect(await screen.findByText(/将恢复执行该规则。当天已处理记录保留/)).toBeTruthy();
  expect(writes()).toHaveLength(0);
  fireEvent.click(screen.getByRole('button', { name: '删除规则' }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('daily-refill', { expectedRevision: 8, action: 'delete', itemId: 3 }));
});
it.each([409, 500])('保存失败 %i 后锁定写入，重新读取才恢复编辑', async status => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => { if (path === 'daily-refill' && payload) throw new ApiError(status, '请重新读取规则。'); return original(path, payload); });
  open(); fireEvent.click(await screen.findByRole('button', { name: /编\s*辑/ }));
  fireEvent.click(await screen.findByRole('button', { name: '保存并生效' }));
  await screen.findByText('请重新读取规则。'); await screen.findByText('请先重新读取生效规则');
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  expect((screen.getByRole('button', { name: /添加补给/ }) as HTMLButtonElement).disabled).toBe(true);
  fireEvent.click(screen.getAllByRole('button', { name: /重新读取生效规则/ }).at(-1)!);
  await waitFor(() => expect((screen.getByRole('button', { name: /添加补给/ }) as HTMLButtonElement).disabled).toBe(false));
  expect(writes()).toHaveLength(1);
});
it('缺少全权限隐藏入口且直接访问不读配置', async () => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => path === 'auth/session' ? { ...identity, permissions: ['GrantCera'] } : original(path, payload));
  open(); await screen.findByText('没有访问此页面的权限');
  expect(within(screen.getByRole('navigation', { name: '主导航' })).queryByText('每日补给')).toBeNull();
  expect(mocked.mock.calls.some(([p]) => p === 'daily-refill')).toBe(false);
});
