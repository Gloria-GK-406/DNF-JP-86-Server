import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { api } from './api';
import { renderApp } from './test-utils';
vi.mock('./api', async original => ({ ...await original<typeof import('./api')>(), api: vi.fn() }));
const mocked = vi.mocked(api);
const identity = { id: 1, loginName: '值班管理员', permissions: ['QueryPlayers', 'SendMail', 'GrantCera', 'ViewAudit'], expiresAt: '2030-01-01T00:00:00Z' };
const record = (index: number, action = 'Login', outcome = 'Rejected', reason = '凭据无效') => ({ operationId: `op-${action}-${index}`,
  occurredAt: `2026-10-16T0${index % 10}:00:00Z`, action, administratorName: null, target: 'GM', outcome, reason });
const overview = { networkEnabled: true, gameReady: true, totalAccounts: 3, totalCharacters: 8, onlineAccounts: 2, onlineCharacters: 9,
  channels: Array.from({ length: 8 }, (_, i) => ({ channelId: i + 1, name: `#ch.${i + 1}`, onlineCharacters: i === 6 ? 5 : i === 7 ? 3 : i === 0 ? 1 : 0 })),
  unmappedOnlineCharacters: 0, sampledAt: '2026-10-16T03:00:00Z' };
let flagged: ReturnType<typeof record>[];
beforeEach(() => {
  sessionStorage.clear();
  flagged = [...Array.from({ length: 8 }, (_, i) => record(i)), record(1, 'ChangeEvent', 'RolledBack', '版本冲突')];
  mocked.mockImplementation(async path => {
    if (path === 'auth/status') return { initialized: true };
    if (path === 'auth/session') return identity;
    if (path === 'overview') return overview;
    if (path.startsWith('audit?') && path.includes('outcome=attention')) return { items: flagged, total: flagged.length, page: 1, pageSize: 100 };
    if (path.startsWith('audit?')) return { items: Array.from({ length: 12 }, (_, i) => record(i, 'SendOfficialMail', 'Succeeded', '')), total: 12, page: 1, pageSize: 20 };
    if (path === 'events') return { entries: [] };
    return undefined;
  });
});
const attentionPanel = () => screen.getByRole('region', { name: '需要处理' });
it('同类合并成一行并可展开明细；单类已读与全部已读只记在本浏览器', async () => {
  renderApp('/');
  const panel = await screen.findByText('2 类 · 9 条').then(() => attentionPanel());
  const logins = within(panel).getByLabelText('登录已拒绝');
  expect(within(logins).getByText('×8')).toBeTruthy();
  fireEvent.click(within(logins).getByRole('button', { name: '展开明细' }));
  expect(within(logins).getAllByText(/凭据无效/).length).toBe(9);
  fireEvent.click(within(logins).getByRole('button', { name: '知道了' }));
  await waitFor(() => expect(within(attentionPanel()).queryByLabelText('登录已拒绝')).toBeNull());
  expect(JSON.parse(localStorage.getItem('dfo.gm.ack.overview')!)).toHaveLength(8);
  fireEvent.click(within(attentionPanel()).getByRole('button', { name: /全部已读/ }));
  expect(await within(attentionPanel()).findByText('都处理完了，喝口水吧')).toBeTruthy();
  expect(mocked.mock.calls.some(([, payload]) => payload !== undefined)).toBe(false);
});
it('频道按人数排序只显示前五个，展开状态在刷新后保留', async () => {
  renderApp('/');
  const toggle = await screen.findByRole('button', { name: /还有 3 个频道 · 共 0 人/ });
  const bars = () => screen.getAllByRole('img', { name: /#ch\.\d+ \d+ 人/ }).map(x => x.getAttribute('aria-label'));
  expect(bars()).toEqual(['#ch.7 5 人', '#ch.8 3 人', '#ch.1 1 人', '#ch.2 0 人', '#ch.3 0 人']);
  fireEvent.click(toggle); expect(bars()).toHaveLength(8);
  cleanup(); renderApp('/');
  await screen.findByRole('button', { name: /收起/ }); await waitFor(() => expect(bars()).toHaveLength(8));
});
it('最近操作默认五条，可展开；结果未确认的发放以横幅钉在顶部', async () => {
  sessionStorage.setItem('dfo.gm.mail.pending.1', JSON.stringify({ requestId: '7c2af8a7-dfaf-4356-8be5-5db7975bf4c5', input: { title: '补偿', body: '', recipientIds: [1], items: [], gold: 0 } }));
  renderApp('/');
  expect(await screen.findByText('官方邮件结果未确认：补偿')).toBeTruthy();
  const recent = screen.getByRole('region', { name: '最近操作' });
  await waitFor(() => expect(within(recent).getAllByText('发送官方邮件')).toHaveLength(5));
  fireEvent.click(within(recent).getByRole('button', { name: /最近 12 条/ }));
  expect(within(recent).getAllByText('发送官方邮件')).toHaveLength(12);
});
