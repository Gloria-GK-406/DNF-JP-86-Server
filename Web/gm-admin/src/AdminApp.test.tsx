import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import zhCN from 'antd/locale/zh_CN';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminApp } from './AdminApp';
import { api, ApiError, type Identity } from './api';
vi.mock('./api', async importOriginal => ({ ...await importOriginal<typeof import('./api')>(), api: vi.fn() }));
const mocked = vi.mocked(api);
const operator: Identity = { id: 1, loginName: '管理员甲', permissions: ['ViewAudit'], expiresAt: '2030-01-01T00:00:00Z' };
function open(path = '/operations') { render(<ConfigProvider locale={zhCN}><App><MemoryRouter initialEntries={[path]}><AdminApp /></MemoryRouter></App></ConfigProvider>); }
function setup(identity: Identity | null = operator) {
  mocked.mockImplementation(async path => {
    if (path === 'auth/status') return { initialized: true };
    if (path === 'auth/session') { if (!identity) throw new ApiError(401, '请先登录'); return identity; }
    if (path.startsWith('audit')) return { items: [{ operationId: 'test-operation', occurredAt: '2026-10-02T00:00:00Z', action: 'Login', administratorName: '管理员甲', target: 'GM', outcome: 'Succeeded', reason: '管理员登录' }], total: 21, page: 1, pageSize: 20 };
    return undefined;
  });
}
async function submitLogin() {
  fireEvent.change(await screen.findByLabelText('管理员账号'), { target: { value: 'operator' } });
  fireEvent.change(screen.getByLabelText('密码'), { target: { value: 'test-password' } });
  fireEvent.click(screen.getByRole('button', { name: /登\s*录/ }));
}
function deferred<T>() {
  let resolve!: (value: T) => void; let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((succeed, fail) => { resolve = succeed; reject = fail; });
  return { promise, resolve, reject };
}
describe('中文管理员交互', () => {
  beforeEach(() => { vi.useRealTimers(); setup(); });
  it('未初始化提示配置并关闭登录表单', async () => {
    mocked.mockImplementation(async () => ({ initialized: false })); open();
    expect(await screen.findByText('管理员尚未初始化')).toBeTruthy(); expect(screen.queryByLabelText('密码')).toBeNull();
  });
  it('错误密码反馈后可以成功登录，并只显示获授权菜单', async () => {
    setup(null); let attempt = 0;
    mocked.mockImplementation(async path => {
      if (path === 'auth/status') return { initialized: true };
      if (path === 'auth/session') throw new ApiError(401, '请先登录');
      if (path === 'auth/login') { if (attempt++ === 0) throw new ApiError(401, '账号或密码错误。'); return operator; }
      return { items: [], total: 0, page: 1, pageSize: 20 };
    }); open(); await submitLogin(); expect(await screen.findByText('账号或密码错误。')).toBeTruthy();
    await submitLogin(); expect(await screen.findByRole('button', { name: '退出登录' })).toBeTruthy();
    expect(screen.queryByText('点券发放')).toBeNull(); expect(screen.queryByText('官方邮件')).toBeNull();
  });
  it('审计显示并且分页请求第二页', async () => {
    open(); expect(await screen.findByText('管理员登录')).toBeTruthy();
    fireEvent.click(screen.getByTitle('下一页'));
    await waitFor(() => expect(mocked).toHaveBeenCalledWith('audit?page=2&pageSize=20'));
  });
  it('权限不足的直接URL不加载私有审计', async () => {
    setup({ ...operator, permissions: ['QueryPlayers'] }); open('/operations');
    expect(await screen.findByText('没有访问此页面的权限')).toBeTruthy();
    expect(mocked.mock.calls.some(([path]) => path.startsWith('audit'))).toBe(false);
  });
  it('审计接口权限撤回时显示拒绝并清空列表', async () => {
    setup(); const original = mocked.getMockImplementation()!;
    mocked.mockImplementation(async path => { if (path.startsWith('audit')) throw new ApiError(403, '没有执行此操作的权限。'); return original(path); }); open();
    expect(await screen.findByText('没有执行此操作的权限。')).toBeTruthy(); expect(screen.queryByText('管理员登录')).toBeNull();
  });
  it('退出后返回登录页', async () => {
    open(); fireEvent.click(await screen.findByRole('button', { name: '退出登录' }));
    expect(await screen.findByLabelText('管理员账号')).toBeTruthy(); expect(mocked).toHaveBeenCalledWith('auth/logout', {});
  });
  it('审计请求会话失效时返回登录并给出提示', async () => {
    setup(); const original = mocked.getMockImplementation()!;
    mocked.mockImplementation(async path => { if (path.startsWith('audit')) throw new ApiError(401, '会话已失效'); return original(path); }); open();
    expect(await screen.findByText('会话已失效，请重新登录。')).toBeTruthy(); expect(screen.getByLabelText('管理员账号')).toBeTruthy();
  });
  it('身份定时检查失效后退出私有页面', async () => {
    let tick: (() => void) | undefined;
    const interval = vi.spyOn(window, 'setInterval').mockImplementation((handler, delay) => { if (delay === 60000) tick = handler as () => void; return 12345; });
    open('/'); await screen.findByRole('button', { name: '退出登录' });
    mocked.mockRejectedValue(new ApiError(401, '过期'));
    await act(async () => { tick!(); });
    expect(screen.getByText('会话已失效，请重新登录。')).toBeTruthy(); interval.mockRestore();
  });
  it('旧身份成功响应晚于退出不能恢复已退出菜单', async () => {
    const pending = deferred<Identity>(); let tick: (() => void) | undefined; let sessionRequests = 0;
    const original = mocked.getMockImplementation()!;
    mocked.mockImplementation(async path => {
      if (path === 'auth/session' && sessionRequests++ > 0) return pending.promise;
      return original(path);
    });
    const interval = vi.spyOn(window, 'setInterval').mockImplementation((handler, delay) => { if (delay === 60000) tick = handler as () => void; return 12345; });
    try {
      open('/'); await screen.findByRole('button', { name: '退出登录' });
      await act(async () => { tick!(); });
      fireEvent.click(screen.getByRole('button', { name: '退出登录' }));
      await screen.findByLabelText('管理员账号');
      await act(async () => { pending.resolve(operator); });
      expect(screen.getByLabelText('管理员账号')).toBeTruthy();
      expect(screen.queryByRole('button', { name: '退出登录' })).toBeNull();
    } finally { interval.mockRestore(); }
  });
  it('旧身份401晚于新登录不能清除新身份及权限', async () => {
    const pending = deferred<Identity>(); let tick: (() => void) | undefined; let sessionRequests = 0;
    const replacement: Identity = { ...operator, id: 2, loginName: '管理员乙', permissions: ['QueryPlayers'] };
    const original = mocked.getMockImplementation()!;
    mocked.mockImplementation(async path => {
      if (path === 'auth/session' && sessionRequests++ > 0) return pending.promise;
      if (path === 'auth/login') return replacement;
      return original(path);
    });
    const interval = vi.spyOn(window, 'setInterval').mockImplementation((handler, delay) => { if (delay === 60000) tick = handler as () => void; return 12345; });
    try {
      open('/'); await screen.findByRole('button', { name: '退出登录' });
      await act(async () => { tick!(); });
      fireEvent.click(screen.getByRole('button', { name: '退出登录' }));
      await submitLogin(); await screen.findByText('管理员乙');
      await act(async () => { pending.reject(new ApiError(401, '旧会话已失效')); });
      expect(screen.getByText('管理员乙')).toBeTruthy();
      expect(screen.getByText('账号与角色')).toBeTruthy();
      expect(screen.queryByLabelText('管理员账号')).toBeNull();
    } finally { interval.mockRestore(); }
  });
});
