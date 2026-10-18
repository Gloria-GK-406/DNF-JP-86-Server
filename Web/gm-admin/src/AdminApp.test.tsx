import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { api, ApiError, type Identity } from './api';
import { deferred, logout, renderApp } from './test-utils';
vi.mock('./api', async importOriginal => ({ ...await importOriginal<typeof import('./api')>(), api: vi.fn() }));
const mocked = vi.mocked(api);
const operator: Identity = { id: 1, loginName: '管理员甲', permissions: ['ViewAudit'], expiresAt: '2030-01-01T00:00:00Z' };
const open = (path = '/operations') => renderApp(path);
function setup(identity: Identity | null = operator) {
  mocked.mockImplementation(async path => {
    if (path === 'auth/status') return { initialized: true };
    if (path === 'auth/session') { if (!identity) throw new ApiError(401, '请先登录'); return identity; }
    if (path.startsWith('audit')) return { items: [{ operationId: 'test-operation', occurredAt: '2026-10-02T00:00:00Z', action: 'Login', administratorName: '管理员甲', target: 'GM', outcome: 'Succeeded', reason: '管理员登录' }], total: 21, page: 1, pageSize: 20 };
    if (path === 'server-info') return { version: '0.2.6', networkEnabled: true, channelPort: 7001, gamePorts: [10011], gmPort: 18081, pvfSha256: 'abcdef0123456789' };
    return undefined;
  });
}
async function submitLogin() {
  fireEvent.change(await screen.findByLabelText('管理员账号'), { target: { value: 'operator' } });
  fireEvent.change(screen.getByLabelText('密码'), { target: { value: 'test-password' } });
  fireEvent.click(screen.getByRole('button', { name: /登\s*录/ }));
}
const nav = () => screen.getByRole('navigation', { name: '主导航' });
describe('中文管理员交互', () => {
  beforeEach(() => { vi.useRealTimers(); setup(); });
  it('未初始化提示配置并关闭登录表单', async () => {
    mocked.mockImplementation(async () => ({ initialized: false })); open();
    expect(await screen.findByText('管理员尚未初始化')).toBeTruthy(); expect(screen.queryByLabelText('密码')).toBeNull();
  });
  it('空账号或空密码不提交，错误密码反馈后可以成功登录，并只显示获授权导航', async () => {
    setup(null); let attempt = 0;
    mocked.mockImplementation(async path => {
      if (path === 'auth/status') return { initialized: true };
      if (path === 'auth/session') throw new ApiError(401, '请先登录');
      if (path === 'auth/login') { if (attempt++ === 0) throw new ApiError(401, '账号或密码错误。'); return operator; }
      return { items: [], total: 0, page: 1, pageSize: 20 };
    }); open();
    fireEvent.click(await screen.findByRole('button', { name: /登\s*录/ }));
    expect(await screen.findByText('请输入管理员账号')).toBeTruthy(); expect(mocked.mock.calls.some(([path]) => path === 'auth/login')).toBe(false);
    await submitLogin(); expect(await screen.findByText('账号或密码错误。')).toBeTruthy();
    await submitLogin(); await screen.findByRole('navigation', { name: '主导航' });
    expect(within(nav()).getByText('操作记录')).toBeTruthy();
    expect(within(nav()).queryByText('发放')).toBeNull(); expect(within(nav()).queryByText('服务概览')).toBeNull();
    expect(mocked).toHaveBeenCalledWith('auth/login', { loginName: 'operator', password: 'test-password' });
  });
  it('审计显示并且分页请求第二页', async () => {
    open(); expect(await screen.findByText('管理员登录')).toBeTruthy();
    fireEvent.click(screen.getByTitle('下一页'));
    await waitFor(() => expect(mocked).toHaveBeenCalledWith('audit?page=2&pageSize=20'));
  });
  it('审计按结果和操作筛选，切换筛选回到第一页', async () => {
    open(); await screen.findByText('管理员登录');
    fireEvent.click(screen.getByText('需关注'));
    await waitFor(() => expect(mocked).toHaveBeenCalledWith('audit?page=1&pageSize=20&outcome=attention'));
    fireEvent.mouseDown(screen.getByRole('combobox', { name: '操作类型' }));
    fireEvent.click((await screen.findAllByText('管理活动')).at(-1)!);
    await waitFor(() => expect(mocked).toHaveBeenCalledWith('audit?page=1&pageSize=20&outcome=attention&action=ChangeEvent'));
  });
  it('权限不足的直接URL不加载私有审计', async () => {
    setup({ ...operator, permissions: ['QueryPlayers'] }); open('/operations');
    expect(await screen.findByText('没有访问此页面的权限')).toBeTruthy();
    expect(mocked.mock.calls.some(([path]) => path.startsWith('audit'))).toBe(false);
  });
  it('审计接口权限撤回时显示拒绝并清空列表', async () => {
    setup(); const original = mocked.getMockImplementation()!;
    mocked.mockImplementation(async path => { if (path.startsWith('audit')) throw new ApiError(403, '没有执行此操作的权限。'); return original(path); }); open();
    expect(await screen.findByText('没有访问此页面的权限')).toBeTruthy(); expect(screen.queryByText('管理员登录')).toBeNull();
    expect(within(nav()).queryByText('操作记录')).toBeNull();
  });
  it('退出后返回登录页', async () => {
    open(); await logout();
    expect(await screen.findByLabelText('管理员账号')).toBeTruthy(); expect(mocked).toHaveBeenCalledWith('auth/logout', {});
  });
  it('审计请求会话失效时返回登录并给出提示', async () => {
    setup(); const original = mocked.getMockImplementation()!;
    mocked.mockImplementation(async path => { if (path.startsWith('audit')) throw new ApiError(401, '会话已失效'); return original(path); }); open();
    expect(await screen.findByText('会话已失效，请重新登录。')).toBeTruthy(); expect(screen.getByLabelText('管理员账号')).toBeTruthy();
  });
  it('身份定时检查失效后退出私有页面', async () => {
    let tick: (() => void) | undefined;
    const interval = vi.spyOn(window, 'setInterval').mockImplementation((handler, delay) => { if (delay === 60000 && !tick) tick = handler as () => void; return 12345; });
    open('/operations'); await screen.findByText('管理员登录');
    mocked.mockRejectedValue(new ApiError(401, '过期'));
    await act(async () => { tick!(); });
    expect(screen.getByText('会话已失效，请重新登录。')).toBeTruthy(); interval.mockRestore();
  });
  it('旧身份成功响应晚于退出不能恢复已退出导航', async () => {
    const pending = deferred<Identity>(); let tick: (() => void) | undefined; let sessionRequests = 0;
    const original = mocked.getMockImplementation()!;
    mocked.mockImplementation(async path => {
      if (path === 'auth/session' && sessionRequests++ > 0) return pending.promise;
      return original(path);
    });
    const interval = vi.spyOn(window, 'setInterval').mockImplementation((handler, delay) => { if (delay === 60000 && !tick) tick = handler as () => void; return 12345; });
    try {
      open('/operations'); await screen.findByText('管理员登录');
      await act(async () => { tick!(); });
      await logout(); await screen.findByLabelText('管理员账号');
      await act(async () => { pending.resolve(operator); });
      expect(screen.getByLabelText('管理员账号')).toBeTruthy();
      expect(screen.queryByRole('navigation', { name: '主导航' })).toBeNull();
    } finally { interval.mockRestore(); }
  });
  it('旧身份401晚于新登录不能清除新身份及权限', async () => {
    const pending = deferred<Identity>(); let tick: (() => void) | undefined; let sessionRequests = 0;
    const replacement: Identity = { ...operator, id: 2, loginName: '管理员乙', permissions: ['QueryPlayers'] };
    const original = mocked.getMockImplementation()!;
    mocked.mockImplementation(async path => {
      if (path === 'auth/session' && sessionRequests++ > 0) return pending.promise;
      if (path === 'auth/login') return replacement;
      if (path === 'overview') return undefined;
      return original(path);
    });
    const interval = vi.spyOn(window, 'setInterval').mockImplementation((handler, delay) => { if (delay === 60000 && !tick) tick = handler as () => void; return 12345; });
    try {
      open('/operations'); await screen.findByText('管理员登录');
      await act(async () => { tick!(); });
      await logout();
      await submitLogin(); await screen.findAllByText('管理员乙');
      await act(async () => { pending.reject(new ApiError(401, '旧会话已失效')); });
      expect(screen.getAllByText('管理员乙').length).toBeGreaterThan(0);
      expect(within(nav()).getByText('账号与角色')).toBeTruthy();
      expect(screen.queryByLabelText('管理员账号')).toBeNull();
    } finally { interval.mockRestore(); }
  });
  it('导轨显示服务端口、版本和 PVF 摘要；旧地址跳到新页面', async () => {
    setup({ ...operator, permissions: ['ViewAudit', 'SendMail'] }); open('/mail');
    await screen.findByRole('heading', { name: '发放' });
    const tape = screen.getByLabelText('服务状态');
    expect(await within(tape).findByText('7001')).toBeTruthy(); expect(within(tape).getByText('10011')).toBeTruthy();
    expect(within(tape).getByText('ABCDEF01')).toBeTruthy(); expect(tape.textContent).toContain('v0.2.6');
  });
  it('外观与色系保存在本浏览器并作用到根元素', async () => {
    open(); await screen.findByText('管理员登录');
    fireEvent.click(screen.getByRole('button', { name: '外观与色系' }));
    fireEvent.click(await screen.findByRole('radio', { name: '靛蓝' }));
    expect(document.documentElement.dataset.accent).toBe('indigo'); expect(localStorage.getItem('dfo.gm.palette')).toBe('indigo');
    fireEvent.click(screen.getByText('深色'));
    expect(document.documentElement.dataset.theme).toBe('dark'); expect(localStorage.getItem('dfo.gm.theme')).toBe('dark');
    fireEvent.click(screen.getByText('跟随系统')); fireEvent.click(screen.getByRole('radio', { name: '青绿' }));
    expect(document.documentElement.dataset.theme).toBeUndefined(); expect(document.documentElement.dataset.accent).toBeUndefined();
  });
});
