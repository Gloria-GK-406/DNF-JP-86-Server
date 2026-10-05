import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App, ConfigProvider, Modal } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { AdminApp } from './AdminApp';
import { api, ApiError } from './api';
import type { RefillList } from './DailyRefillView';

vi.mock('./api', async original => ({ ...await original<typeof import('./api')>(), api: vi.fn() }));
const mocked = vi.mocked(api);
const identity = { id: 1, loginName: '补给管理员', permissions: ['QueryPlayers', 'SendMail', 'GrantCera', 'ViewAudit'], expiresAt: '2030-01-01T00:00:00Z' };
let data: RefillList;
afterEach(() => Modal.destroyAll());
function open() { return render(<ConfigProvider theme={{ token: { motion: false } }}><App><MemoryRouter initialEntries={['/daily-refill']}><AdminApp /></MemoryRouter></App></ConfigProvider>); }
beforeEach(() => {
  data = { revision: 8, entries: [
    { itemId: 3, name: '邀请函', source: 'pvf', quantity: 2, mode: 0, expiresAt: null, status: 'overridden', pvfEnabled: true },
    { itemId: 3, name: '邀请函', source: 'custom', quantity: 8, mode: 1, expiresAt: null, status: 'active', pvfEnabled: true },
    { itemId: 4, name: '旧补给', source: 'pvf', quantity: 0, mode: 0, expiresAt: '2000-01-01T00:00:00Z', status: 'expired', pvfEnabled: true },
  ], updatedAt: null, updatedBy: '补给管理员' };
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'auth/status') return { initialized: true }; if (path === 'auth/session') return identity;
    if (path === 'daily-refill') { if (payload) data.revision++; return structuredClone(data); }
    if (path.startsWith('daily-refill/items?')) return { items: [{ itemId: 7, name: '补给药水', category: 'consumable', level: 1, rarity: 1 }], hasMore: false };
    if (path === 'daily-refill/items/7') return { item: { itemId: 7, name: '补给药水', category: 'consumable', level: 1, rarity: 1 }, kind: 'stackable', equipment: null };
    throw new Error('Unexpected request: ' + path);
  });
});
it('默认展示 PVF 和自定义来源，包括覆盖与过期状态', async () => {
  open(); await screen.findByText('每日补给规则');
  expect(await screen.findByText('已被自定义覆盖')).toBeTruthy();
  expect(screen.getByText('自定义')).toBeTruthy(); expect(screen.getByText('已过期')).toBeTruthy();
  expect(screen.getByText('增量发放')).toBeTruthy(); expect(screen.queryByRole('dialog')).toBeNull();
  expect(screen.getByText(/整理背包或重登不会补发/)).toBeTruthy();
});
it('新增通过补给专用目录选择物品并保存数量、方式及原版本', async () => {
  open(); fireEvent.click(await screen.findByRole('button', { name: '添加补给' }));
  fireEvent.click(await screen.findByRole('button', { name: '选择补给物品' }));
  const picker = (await screen.findByText('选择每日补给物品', { selector: '.ant-modal-title' })).closest('[role=dialog]') as HTMLElement;
  fireEvent.click(await within(picker).findByLabelText('选择物品 7'));
  fireEvent.click(await within(picker).findByRole('button', { name: '选择此补给物品' }));
  await waitFor(() => expect(screen.queryByText('选择每日补给物品', { selector: '.ant-modal-title' })).toBeNull());
  fireEvent.change(screen.getByLabelText('目标数量'), { target: { value: '12' } });
  fireEvent.mouseDown(screen.getByLabelText('补给方式'));
  fireEvent.click(await screen.findByText('增量发放：额外发放指定数量'));
  fireEvent.click(screen.getByRole('button', { name: '保存并生效' }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('daily-refill', { expectedRevision: 8, action: 'add', itemId: 7, quantity: 12, mode: 1, expiresAt: null }));
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  expect(mocked.mock.calls.some(([p]) => p.startsWith('mail/') || p.startsWith('initialization/'))).toBe(false);
});
it('删除自定义告知 PVF 恢复且保留当天记录，确认才发请求', async () => {
  open(); fireEvent.click(await screen.findByRole('button', { name: /删\s*除/ }));
  expect(await screen.findByText(/将恢复执行该规则。当天已处理记录保留/)).toBeTruthy();
  expect(mocked.mock.calls.filter(([p, payload]) => p === 'daily-refill' && payload)).toHaveLength(0);
  fireEvent.click(screen.getByRole('button', { name: '删除规则' }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('daily-refill', { expectedRevision: 8, action: 'delete', itemId: 3 }));
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
});
it.each([409, 500])('保存失败 %i 后禁止再次提交，重新读取才恢复编辑', async status => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => { if (path === 'daily-refill' && payload) throw new ApiError(status, '请重新读取规则。'); return original(path, payload); });
  open(); fireEvent.click(await screen.findByRole('button', { name: /编\s*辑/ }));
  fireEvent.click(await screen.findByRole('button', { name: '保存并生效' }));
  await screen.findByText('请重新读取规则。');
  expect(screen.queryByRole('button', { name: '保存并生效' })).toBeNull();
  fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: '重新读取生效规则' }));
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  await waitFor(() => expect((screen.getByRole('button', { name: '添加补给' }) as HTMLButtonElement).disabled).toBe(false));
  expect(mocked.mock.calls.filter(([p, payload]) => p === 'daily-refill' && payload)).toHaveLength(1);
});
it('缺少全权限隐藏入口且直接访问不读配置', async () => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => path === 'auth/session' ? { ...identity, permissions: ['GrantCera'] } : original(path, payload));
  open(); await screen.findByText('当前页面不可访问');
  expect(screen.queryByRole('menuitem', { name: '每日补给' })).toBeNull();
  expect(mocked.mock.calls.some(([p]) => p === 'daily-refill')).toBe(false);
});
