import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import zhCN from 'antd/locale/zh_CN';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import { AdminApp } from './AdminApp';
import { api, ApiError, type CharacterDetail, type CharacterView } from './api';
vi.mock('./api', async original => ({ ...await original<typeof import('./api')>(), api: vi.fn() }));
const mocked = vi.mocked(api);
const identity = { id: 1, loginName: '查询管理员', permissions: ['QueryPlayers'], expiresAt: '2030-01-01T00:00:00Z' };
const character: CharacterView = { characterId: 1, accountId: 7, loginName: 'player07', name: '角色甲', nameDecodeStatus: 'valid', nameBytesHex: null,
  job: 1, growType: 2, level: 86, isDeleted: false, isOnline: true, channel: { channelId: 11, name: '频道甲' },
  position: { source: 'current', townId: 3, areaId: 4, x: 123, y: 234, direction: 1 }, sampledAt: '2026-10-02T00:00:00Z' };
const detail: CharacterDetail = { ...character, balances: { cera: 2147483647, tokenCera: 0, happyTokenCera: 12 } };
function open(path = '/characters') { render(<ConfigProvider locale={zhCN} theme={{ token: { motion: false } }}><App><MemoryRouter initialEntries={[path]}><AdminApp /></MemoryRouter></App></ConfigProvider>); }
function populated() {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => {
    if (path === 'characters/1') return detail;
    if (path === 'accounts/7') return { accountId: 7, loginName: 'player07', characterCount: 1, balances: detail.balances, isOnline: true, activeCharacterId: 1, sampledAt: character.sampledAt };
    if (path.startsWith('characters?')) return { items: [character], total: 21, page: 1, pageSize: 20, sampledAt: character.sampledAt };
    return original(path);
  });
}
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done; }); return { promise, resolve }; }
beforeEach(() => {
  mocked.mockImplementation(async path => {
    if (path === 'auth/status') return { initialized: true };
    if (path === 'auth/session') return identity;
    return { items: [], total: 0, page: 1, pageSize: 20, sampledAt: '2026-10-02T00:00:00Z' };
  });
});
it('角色查询提供可用中文输入和空结果', async () => {
  open();
  expect(await screen.findByLabelText('角色姓名搜索')).toBeTruthy();
  expect(await screen.findByText('没有符合条件的角色')).toBeTruthy();
});

it('接口失权清除列表和已打开详情', async () => {
  populated(); let revoked = false; const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => { if (revoked && path === 'characters/1') throw new ApiError(403, '没有执行此操作的权限。'); return original(path); });
  open(); await screen.findByText('角色甲'); fireEvent.click(screen.getByRole('button', { name: '查看详情' }));
  await screen.findByText('姓名诊断'); revoked = true;
  fireEvent.click(screen.getByRole('button', { name: /刷新详情/ }));
  await waitFor(() => expect(screen.queryByText('姓名诊断')).toBeNull());
  expect(screen.queryByText('角色甲')).toBeNull(); expect(await screen.findByText('没有访问此页面的权限')).toBeTruthy();
});

it('搜索与在线过滤重置分页且字面条件进入请求', async () => {
  populated(); open(); await screen.findByText('角色甲'); fireEvent.click(screen.getByTitle('下一页'));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('characters?page=2&pageSize=20'));
  fireEvent.change(screen.getByLabelText('角色姓名搜索'), { target: { value: '中文%_' } });
  fireEvent.click(screen.getByLabelText('仅在线')); fireEvent.click(screen.getByRole('button', { name: /查\s*询/ }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('characters?page=1&pageSize=20&q=%E4%B8%AD%E6%96%87%25_&onlineOnly=true'));
  fireEvent.click(screen.getByRole('button', { name: /重\s*置/ }));
  await waitFor(() => expect((screen.getByLabelText('角色姓名搜索') as HTMLInputElement).value).toBe(''));
});

it('详情按稳定ID关联账号和所属角色列表并支持刷新关闭', async () => {
  populated(); open(); await screen.findByText('角色甲'); fireEvent.click(screen.getByRole('button', { name: '查看详情' }));
  await screen.findByText('姓名诊断'); expect(screen.getByText('点券：2147483647')).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: /刷新详情/ }));
  await waitFor(() => expect(mocked.mock.calls.filter(([path]) => path === 'characters/1').length).toBe(2));
  fireEvent.click(screen.getByRole('button', { name: '查看归属账号' })); await screen.findByRole('button', { name: '查看该账号角色' });
  expect(mocked).toHaveBeenCalledWith('accounts/7');
  fireEvent.click(screen.getByRole('button', { name: '查看该账号角色' }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('characters?page=1&pageSize=20&accountId=7'));
  fireEvent.click(screen.getByRole('button', { name: '查看详情' })); await screen.findByText('姓名诊断');
  fireEvent.click(screen.getByRole('button', { name: '关闭' })); await waitFor(() => expect(screen.queryByText('姓名诊断')).toBeNull());
  fireEvent.click(screen.getByRole('button', { name: /刷新列表/ }));
  await waitFor(() => expect(mocked.mock.calls.filter(([path]) => path === 'characters?page=1&pageSize=20&accountId=7').length).toBe(2));
});

it('账号切换及登录名搜索使用账号分页契约', async () => {
  open(); await screen.findByLabelText('角色姓名搜索'); fireEvent.click(screen.getByLabelText('账号'));
  fireEvent.change(await screen.findByLabelText('账号登录名搜索'), { target: { value: 'player%' } });
  fireEvent.click(screen.getByRole('button', { name: /查\s*询/ }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('accounts?page=1&pageSize=20&q=player%25'));
  expect(await screen.findByText('没有符合条件的账号')).toBeTruthy();
});

it.each([[400, '查询参数无效。'], [500, '查询失败，请稍后重试。'], [0, '网络断开']])('查询失败%s明确反馈并清除旧数据', async (status, message) => {
  populated(); let failed = false; const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => { if (failed && path.startsWith('characters?')) throw status ? new ApiError(status, message) : new Error(message); return original(path); });
  open(); await screen.findByText('角色甲'); failed = true; fireEvent.click(screen.getByRole('button', { name: /刷新列表/ }));
  expect(await screen.findByText(message)).toBeTruthy(); expect(screen.queryByText('角色甲')).toBeNull();
});

it('详情不存在显示404说明，查询会话失效回登录页', async () => {
  populated(); const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => { if (path === 'characters/1') throw new ApiError(404, '角色不存在。'); return original(path); });
  open(); await screen.findByText('角色甲'); fireEvent.click(screen.getByRole('button', { name: '查看详情' }));
  expect(await screen.findByText('角色不存在。')).toBeTruthy();
  mocked.mockRejectedValue(new ApiError(401, '会话失效')); fireEvent.click(screen.getByRole('button', { name: /刷新列表/ }));
  expect(await screen.findByLabelText('管理员账号')).toBeTruthy(); expect(screen.queryByText('角色甲')).toBeNull();
});

it('快速连续查询的旧成功响应不能覆盖新结果', async () => {
  const old = deferred<unknown>(); const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => {
    if (path === 'characters?page=1&pageSize=20') return old.promise;
    if (path.includes('q=')) return { items: [{ ...character, name: '新角色' }], total: 1, page: 1, pageSize: 20, sampledAt: character.sampledAt };
    return original(path);
  });
  open(); fireEvent.change(await screen.findByLabelText('角色姓名搜索'), { target: { value: '新' } }); fireEvent.click(screen.getByRole('button', { name: /查\s*询/ }));
  await screen.findByText('新角色'); await act(async () => { old.resolve({ items: [{ ...character, name: '旧角色' }], total: 1, page: 1, pageSize: 20, sampledAt: character.sampledAt }); });
  expect(screen.getByText('新角色')).toBeTruthy(); expect(screen.queryByText('旧角色')).toBeNull();
});

it('退出后的旧列表响应不能恢复私有资料', async () => {
  const pending = deferred<unknown>(); const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => path.startsWith('characters?') ? pending.promise : original(path));
  open(); fireEvent.click(await screen.findByRole('button', { name: '退出登录' })); await screen.findByLabelText('管理员账号');
  await act(async () => { pending.resolve({ items: [character], total: 1, page: 1, pageSize: 20, sampledAt: character.sampledAt }); });
  expect(screen.queryByText('角色甲')).toBeNull(); expect(screen.getByLabelText('管理员账号')).toBeTruthy();
});

it('概览展示游戏关闭与真实人数并可刷新', async () => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => path === 'overview' ? { networkEnabled: false, gameReady: false, totalAccounts: 45, totalCharacters: 47,
    onlineAccounts: 2, onlineCharacters: 1, channels: [{ channelId: 11, name: '频道甲', onlineCharacters: 1 }], unmappedOnlineCharacters: 0, sampledAt: character.sampledAt } : original(path));
  open('/'); await screen.findByText('游戏网络：已关闭；游戏服务：未就绪'); expect(screen.getByText('频道甲')).toBeTruthy(); expect(screen.getByText(/采样时间/)).toBeTruthy();
  fireEvent.click(await screen.findByRole('button', { name: '刷新概览' })); await waitFor(() => expect(mocked.mock.calls.filter(([path]) => path === 'overview').length).toBe(2));
});

it('身份权限撤回后旧详情成功响应不能恢复列表或详情', async () => {
  populated(); const pending = deferred<unknown>(); let revoked = false; let tick: (() => void) | undefined;
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => {
    if (path === 'auth/session' && revoked) return { ...identity, permissions: [] };
    if (path === 'characters/1') return pending.promise;
    return original(path);
  });
  const interval = vi.spyOn(window, 'setInterval').mockImplementation((handler, delay) => { if (delay === 60000) tick = handler as () => void; return 321; });
  try {
    open(); await screen.findByText('角色甲'); fireEvent.click(screen.getByRole('button', { name: '查看详情' }));
    await waitFor(() => expect(mocked).toHaveBeenCalledWith('characters/1')); revoked = true;
    await act(async () => { tick!(); }); await screen.findByText('没有访问此页面的权限');
    await act(async () => { pending.resolve(detail); });
    expect(screen.queryByText('角色甲')).toBeNull(); expect(screen.queryByText('姓名诊断')).toBeNull();
    expect(screen.getByRole('button', { name: '退出登录' })).toBeTruthy();
  } finally { interval.mockRestore(); }
});

it('无效ID在页面说明，姓名HTML按文本显示', async () => {
  populated(); const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => path.startsWith('characters?') ? { items: [{ ...character, name: '<img src=x onerror=alert(1)>' }], total: 1, page: 1, pageSize: 20, sampledAt: character.sampledAt } : original(path));
  open(); await screen.findByText('<img src=x onerror=alert(1)>'); expect(document.querySelector('img[src="x"]')).toBeNull();
  fireEvent.change(screen.getByLabelText('角色 ID'), { target: { value: '0' } }); fireEvent.click(screen.getByRole('button', { name: /查\s*询/ }));
  expect(await screen.findByText('请输入有效正整数 ID')).toBeTruthy(); expect(mocked.mock.calls.filter(([path]) => path.startsWith('characters?')).length).toBe(1);
});

it('403后的旧身份轮询成功不能恢复查询权限', async () => {
  populated(); const stale = deferred<unknown>(); let sessionRequests = 0; let tick: (() => void) | undefined;
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => {
    if (path === 'auth/session' && sessionRequests++ > 0) return stale.promise;
    if (path === 'characters/1') throw new ApiError(403, '没有执行此操作的权限。');
    return original(path);
  });
  const interval = vi.spyOn(window, 'setInterval').mockImplementation((handler, delay) => { if (delay === 60000) tick = handler as () => void; return 456; });
  try {
    open(); await screen.findByText('角色甲'); await act(async () => { tick!(); });
    fireEvent.click(screen.getByRole('button', { name: '查看详情' })); await screen.findByText('没有访问此页面的权限');
    await act(async () => { stale.resolve(identity); });
    expect(screen.getByText('没有访问此页面的权限')).toBeTruthy(); expect(screen.queryByText('角色甲')).toBeNull();
    expect(screen.queryByRole('menuitem', { name: '账号与角色' })).toBeNull(); expect(screen.getByRole('button', { name: '退出登录' })).toBeTruthy();
  } finally { interval.mockRestore(); }
});
