import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { api, ApiError, type Identity } from './api';
import type { PlayerMe } from './playerApi';
import { refuse, renderPortal, stubPlayerFetch, type PlayerCall, type PlayerReply } from './test-utils';
vi.mock('./api', async original => ({ ...await original<typeof import('./api')>(), api: vi.fn() }));
const mocked = vi.mocked(api);
const operator: Identity = { id: 9, loginName: '管理员甲', permissions: ['ViewAudit'], expiresAt: '2030-01-01T00:00:00Z' };
const player = { accountId: 1, loginName: 'player01', expiresAt: '2030-01-01T00:00:00Z' };
const me: PlayerMe = { accountId: 1, loginName: 'player01', balances: { cera: 1000, tokenCera: 50 },
  characters: [{ characterId: 11, name: '角色一', job: 0, level: 70, isOnline: true }],
  portal: { ceraEnabled: true, mailEnabled: true, limits: { ceraPerGrant: 0, ceraPerDay: 0, tokenCeraPerGrant: 0, tokenCeraPerDay: 0, mailGoldPerMail: 0, mailsPerDay: 0 },
    today: { cera: 0, tokenCera: 0, mails: 0 } } };
let enabled: boolean; let session: boolean; let gmSession: Identity | null;
let extra: (call: PlayerCall) => PlayerReply | undefined;
let calls: PlayerCall[];
beforeEach(() => {
  enabled = true; session = false; gmSession = null; extra = () => undefined;
  mocked.mockImplementation(async path => {
    if (path === 'auth/status') return { initialized: true };
    if (path === 'auth/session') { if (!gmSession) throw new ApiError(401, '请先登录'); return gmSession; }
    if (path === 'auth/login') return operator;
    if (path.startsWith('audit')) return { items: [], total: 0, page: 1, pageSize: 20 };
    return undefined;
  });
  calls = stubPlayerFetch(call => {
    const custom = extra(call); if (custom) return custom;
    if (call.path === 'auth/status') return { body: { enabled } };
    if (!enabled) return refuse(403, 'portal_closed', '玩家后台已关闭。');
    if (call.path === 'auth/session') return session ? { body: player } : refuse(401, 'unauthenticated', '请先登录。');
    if (call.path === 'auth/login') { session = true; return { body: player }; }
    if (call.path === 'auth/logout') { session = false; return { status: 204 }; }
    if (call.path === 'me') return session ? { body: me } : refuse(401, 'unauthenticated', '请先登录。');
    return undefined;
  });
});
afterEach(() => { vi.unstubAllGlobals(); });
const loginAs = async (name: string) => {
  fireEvent.change(await screen.findByLabelText(name), { target: { value: 'player01' } });
  fireEvent.change(screen.getByLabelText('密码'), { target: { value: 'test-password' } });
  fireEvent.click(screen.getByRole('button', { name: /登\s*录/ }));
};
const playerOption = () => screen.queryByText('玩家', { selector: '.ant-segmented-item-label' });

describe('登录页与顶层壳', () => {
  it('玩家后台开启时登录页默认玩家，可切到管理员并走管理员登录', async () => {
    renderPortal();
    expect(await screen.findByLabelText('账号')).toBeTruthy();
    expect(playerOption()).toBeTruthy(); expect(screen.queryByLabelText('管理员账号')).toBeNull();
    fireEvent.click(screen.getByText('管理员'));
    await loginAs('管理员账号');
    await screen.findByRole('navigation', { name: '主导航' });
    expect(mocked).toHaveBeenCalledWith('auth/login', { loginName: 'player01', password: 'test-password' });
    expect(calls.some(call => call.path === 'auth/login')).toBe(false);
  });
  it.each([['关闭', () => { enabled = false; }], ['状态读取失败', () => { extra = call => { if (call.path === 'auth/status') throw new TypeError('offline'); return undefined; }; }]])(
    '玩家后台%s时登录页不显示玩家选项', async (_, arrange) => {
      arrange(); renderPortal();
      expect(await screen.findByLabelText('管理员账号')).toBeTruthy();
      expect(playerOption()).toBeNull(); expect(screen.queryByText('登录身份')).toBeNull();
    });
  it('管理员模式下管理服务还在连接时只转圈管理员表单，保留身份切换', async () => {
    const original = mocked.getMockImplementation()!;
    mocked.mockImplementation(path => path === 'auth/status' ? new Promise(() => {}) : original(path));
    renderPortal();
    expect(await screen.findByLabelText('账号')).toBeTruthy();
    fireEvent.click(screen.getByText('管理员'));
    expect(await screen.findByText('正在连接管理服务…')).toBeTruthy();
    expect(playerOption()).toBeTruthy(); expect(screen.queryByLabelText('管理员账号')).toBeNull();
    fireEvent.click(playerOption()!);
    expect(await screen.findByLabelText('账号')).toBeTruthy(); expect(screen.queryByText('正在连接管理服务…')).toBeNull();
  });
  it('玩家登录后进入玩家界面而不是管理员界面', async () => {
    renderPortal(); await loginAs('账号');
    expect(await screen.findByRole('navigation', { name: '玩家导航' })).toBeTruthy();
    expect(screen.getByRole('navigation', { name: '底部导航' })).toBeTruthy();
    expect(screen.queryByRole('navigation', { name: '主导航' })).toBeNull();
    expect(calls.find(call => call.path === 'auth/login')).toMatchObject({ method: 'POST', csrf: 'player-token', body: { loginName: 'player01', password: 'test-password' } });
    expect(await screen.findAllByText('player01')).toBeTruthy();
    expect(mocked.mock.calls.map(([path]) => path).every(path => path === 'auth/status' || path === 'auth/session')).toBe(true);
  });
  it('已有玩家会话时直接进入玩家界面，即使同时有管理员会话', async () => {
    session = true; gmSession = operator; renderPortal('/');
    expect(await screen.findByRole('navigation', { name: '玩家导航' })).toBeTruthy();
    expect(screen.queryByRole('navigation', { name: '主导航' })).toBeNull();
  });
  it('玩家凭据错误留在登录页显示服务端消息，不当作会话失效', async () => {
    extra = call => call.path === 'auth/login' ? refuse(401, 'unauthenticated', '账号或密码错误。') : undefined;
    renderPortal(); await loginAs('账号');
    expect(await screen.findByText('账号或密码错误。')).toBeTruthy();
    expect(screen.getByLabelText('账号')).toBeTruthy(); expect(screen.queryByText('会话已失效，请重新登录。')).toBeNull();
  });
  it('登录时玩家后台刚被关闭则进入已关闭页', async () => {
    extra = call => call.path === 'auth/login' ? refuse(403, 'portal_closed', '玩家后台已关闭。') : undefined;
    renderPortal(); await loginAs('账号');
    expect(await screen.findByText('玩家后台已关闭')).toBeTruthy();
  });
  it('读取得到已关闭时切到已关闭页，返回登录后不再显示玩家选项', async () => {
    session = true; extra = call => call.path === 'me' ? (enabled = false, refuse(403, 'portal_closed', '玩家后台已关闭。')) : undefined;
    renderPortal();
    expect(await screen.findByText('玩家后台已关闭')).toBeTruthy();
    expect(screen.queryByRole('navigation', { name: '玩家导航' })).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: '返回登录' }));
    expect(await screen.findByLabelText('管理员账号')).toBeTruthy(); expect(playerOption()).toBeNull();
  });
  it('会话失效时回到登录页并提示', async () => {
    session = true; extra = call => call.path === 'me' ? refuse(401, 'unauthenticated', '请先登录。') : undefined;
    renderPortal();
    expect(await screen.findByText('会话已失效，请重新登录。')).toBeTruthy();
    expect(await screen.findByLabelText('账号')).toBeTruthy();
  });
  it('总开关关闭时登出被拒，仍清空玩家界面进入已关闭页', async () => {
    session = true; renderPortal(); await screen.findByRole('navigation', { name: '玩家导航' });
    extra = call => call.path === 'auth/logout' ? refuse(403, 'portal_closed', '玩家后台已关闭。') : undefined;
    fireEvent.click(screen.getByRole('button', { name: '退出' }));
    expect(await screen.findByText('玩家后台已关闭')).toBeTruthy();
    expect(screen.queryByRole('navigation', { name: '玩家导航' })).toBeNull();
  });
  it('玩家退出后回到登录页，管理员会话仍有效时直接回管理台', async () => {
    session = true; gmSession = operator; renderPortal(); await screen.findByRole('navigation', { name: '玩家导航' });
    fireEvent.click(screen.getByRole('button', { name: '退出' }));
    const nav = await screen.findByRole('navigation', { name: '主导航' });
    expect(within(nav).getByText('操作记录')).toBeTruthy();
    await waitFor(() => expect(calls.some(call => call.path === 'auth/logout' && call.method === 'POST')).toBe(true));
  });
});
