import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { api, ApiError, type Identity, type Permission } from './api';
import type { PlayerPortalSettings } from './playerApi';
import { renderApp } from './test-utils';
vi.mock('./api', async original => ({ ...await original<typeof import('./api')>(), api: vi.fn() }));
const mocked = vi.mocked(api);
const all: Permission[] = ['QueryPlayers', 'SendMail', 'GrantCera', 'ViewAudit'];
const zero = { ceraPerGrant: 0, ceraPerDay: 0, tokenCeraPerGrant: 0, tokenCeraPerDay: 0, mailGoldPerMail: 0, mailsPerDay: 0 };
let identity: Identity;
let settings: PlayerPortalSettings;
let save: (payload: PlayerPortalSettings) => PlayerPortalSettings;
const sessions = [{ accountId: 1, loginName: 'player01', createdAt: '2026-11-13T01:00:00Z', expiresAt: '2026-11-13T09:00:00Z' },
  { accountId: 2, loginName: 'player02', createdAt: '2026-11-13T02:00:00Z', expiresAt: '2026-11-13T10:00:00Z' }];
beforeEach(() => {
  identity = { id: 1, loginName: '管理员甲', permissions: all, expiresAt: '2030-01-01T00:00:00Z' };
  settings = { enabled: true, ceraEnabled: true, mailEnabled: true, limits: { ...zero }, revision: 3 };
  save = payload => ({ ...payload, revision: payload.revision + 1 });
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'auth/status') return { initialized: true };
    if (path === 'auth/session') return identity;
    if (path === 'player-portal/settings') { if (payload) settings = save(structuredClone(payload as PlayerPortalSettings)); return structuredClone(settings); }
    if (path === 'player-portal/sessions') return sessions;
    if (path === 'player-portal/sessions/kick') return { kicked: 1 };
    if (path === 'player-portal/sessions/kick-all') return { kicked: 2 };
    return undefined;
  });
});
const posts = () => mocked.mock.calls.filter(([path, payload]) => path === 'player-portal/settings' && payload).map(([, payload]) => payload);
const saveButton = () => screen.getByRole('button', { name: /保\s*存/ }) as HTMLButtonElement;
async function open() { renderApp('/player-portal'); await screen.findByText('player01'); await waitFor(() => expect(screen.getByRole('switch', { name: '玩家后台' })).toBeTruthy()); }

describe('管理员「玩家后台」页', () => {
  it('只对全部权限管理员可见', async () => {
    identity = { ...identity, permissions: ['QueryPlayers', 'SendMail', 'GrantCera'] };
    renderApp('/');
    const nav = await screen.findByRole('navigation', { name: '主导航' });
    expect(within(nav).queryByText('玩家后台')).toBeNull();
    expect(mocked.mock.calls.some(([path]) => String(path).startsWith('player-portal'))).toBe(false);
  });
  it('切换总开关即提交：限额取已保存的值，不带未保存的草稿', async () => {
    await open();
    fireEvent.change(screen.getByLabelText('点券每日'), { target: { value: '1500' } });
    fireEvent.click(screen.getByRole('switch', { name: '玩家后台' }));
    await waitFor(() => expect(posts()).toHaveLength(1));
    expect(posts()[0]).toEqual({ enabled: false, ceraEnabled: true, mailEnabled: true, limits: zero, revision: 3 });
    await waitFor(() => expect(screen.getByRole('switch', { name: '玩家后台' }).getAttribute('aria-checked')).toBe('false'));
    fireEvent.click(screen.getByRole('switch', { name: '加点券' }));
    await waitFor(() => expect(posts()).toHaveLength(2));
    expect(posts()[1]).toEqual({ enabled: false, ceraEnabled: false, mailEnabled: true, limits: zero, revision: 4 });
  });
  it('保存限额带草稿与读到的版本', async () => {
    await open();
    expect(saveButton().disabled).toBe(true);
    fireEvent.change(screen.getByLabelText('点券每日'), { target: { value: '1500' } });
    await waitFor(() => expect(saveButton().disabled).toBe(false));
    fireEvent.click(saveButton());
    await waitFor(() => expect(posts()).toHaveLength(1));
    expect(posts()[0]).toEqual({ enabled: true, ceraEnabled: true, mailEnabled: true, limits: { ...zero, ceraPerDay: 1500 }, revision: 3 });
    expect(await screen.findByText('已保存')).toBeTruthy();
  });
  it.each([[409, '设置已被修改'], [500, '结果未确认']])('保存返回 %s 时锁定写入，重新读取后解锁', async (status, title) => {
    save = () => { throw new ApiError(status, status === 409 ? '设置已被其他管理员修改。' : '玩家后台设置保存或读取失败，请重新读取确认当前生效设置。'); };
    await open();
    fireEvent.change(screen.getByLabelText('邮件每日封数'), { target: { value: '3' } }); fireEvent.focusOut(screen.getByLabelText('邮件每日封数'));
    await waitFor(() => expect(saveButton().disabled).toBe(false));
    fireEvent.click(saveButton());
    const banner = (await screen.findByText(title)).closest('.banner') as HTMLElement;
    expect(saveButton().disabled).toBe(true);
    expect((screen.getByRole('switch', { name: '玩家后台' }) as HTMLButtonElement).disabled).toBe(true);
    settings = { ...settings, revision: 9, limits: { ...zero, mailsPerDay: 5 } };
    fireEvent.click(within(banner).getByRole('button', { name: /重新读取/ }));
    await waitFor(() => expect(screen.queryByText(title)).toBeNull());
    await waitFor(() => expect((screen.getByLabelText('邮件每日封数') as HTMLInputElement).value).toBe('5'));
    expect((screen.getByRole('switch', { name: '玩家后台' }) as HTMLButtonElement).disabled).toBe(false);
  });
  it('踢单个账号与全部踢下线', async () => {
    await open();
    fireEvent.click(screen.getByRole('button', { name: '踢下线 player01' }));
    await waitFor(() => expect(mocked).toHaveBeenCalledWith('player-portal/sessions/kick', { accountId: 1 }));
    expect(await screen.findByText('已踢下线 1 个会话')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: /全部踢下线/ }));
    const pop = await screen.findByRole('tooltip');
    expect(within(pop).getByText('全部踢下线？')).toBeTruthy();
    expect(mocked.mock.calls.some(([path]) => path === 'player-portal/sessions/kick-all')).toBe(false);
    fireEvent.click(within(pop).getByRole('button', { name: /全部踢下线/ }));
    await waitFor(() => expect(mocked).toHaveBeenCalledWith('player-portal/sessions/kick-all', {}));
  });
});
