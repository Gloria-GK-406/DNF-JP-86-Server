import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { api, ApiError, type CharacterDetail, type CharacterView } from './api';
import { clickWhenIdle, deferred, logout, renderApp } from './test-utils';
vi.mock('./api', async original => ({ ...await original<typeof import('./api')>(), api: vi.fn() }));
const mocked = vi.mocked(api);
const identity = { id: 1, loginName: '查询管理员', permissions: ['QueryPlayers'], expiresAt: '2030-01-01T00:00:00Z' };
const character: CharacterView = { characterId: 1, accountId: 7, loginName: 'player07', name: '角色甲', nameDecodeStatus: 'valid', nameBytesHex: null,
  job: 1, growType: 2, level: 86, isDeleted: false, isOnline: true, channel: { channelId: 11, name: '频道甲' },
  position: { source: 'current', townId: 3, areaId: 4, x: 123, y: 234, direction: 1 }, sampledAt: '2026-10-02T00:00:00Z' };
const detail: CharacterDetail = { ...character, balances: { cera: 2147483647, tokenCera: 0, happyTokenCera: 12 } };
const open = (path = '/players') => renderApp(path);
const search = () => screen.findByLabelText('角色搜索');
const detailPanel = () => screen.findByRole('region', { name: '角色详情' });
function populated() {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => {
    if (path === 'characters/1') return detail;
    if (path === 'accounts/7') return { accountId: 7, loginName: 'player07', characterCount: 1, balances: detail.balances, isOnline: true, activeCharacterId: 1, sampledAt: character.sampledAt };
    if (path.startsWith('characters?')) return { items: [character], total: 21, page: 1, pageSize: 20, sampledAt: character.sampledAt };
    return original(path);
  });
}
beforeEach(() => {
  mocked.mockImplementation(async path => {
    if (path === 'auth/status') return { initialized: true };
    if (path === 'auth/session') return identity;
    if (path === 'overview' || path === 'server-info' || path === 'items/icon-pack') return undefined;
    return { items: [], total: 0, page: 1, pageSize: 20, sampledAt: '2026-10-02T00:00:00Z' };
  });
});
it('角色查询提供中文输入和空结果，转职按 PVF 名称显示', async () => {
  open(); expect(await search()).toBeTruthy();
  expect(await screen.findByText('没找到，换个关键词试试')).toBeTruthy();
  populated(); await clickWhenIdle(() => screen.getByRole('button', { name: /刷新列表/ }));
  expect((await screen.findAllByText(/散打/)).length).toBeGreaterThan(0); expect(screen.getAllByText('格斗家（女）').length).toBeGreaterThan(0);
});
it('接口失权清除列表和已打开详情', async () => {
  populated(); let revoked = false; const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => { if (revoked && path === 'characters/1') throw new ApiError(403, '没有执行此操作的权限。'); return original(path); });
  open(); const panel = await detailPanel(); await within(panel).findByText('姓名编码'); revoked = true;
  await clickWhenIdle(() => within(panel).getByRole('button', { name: '刷新详情' }));
  expect(await screen.findByText('没有访问此页面的权限')).toBeTruthy();
  expect(screen.queryByText('姓名编码')).toBeNull(); expect(screen.queryByText('角色甲')).toBeNull();
});
it('搜索与在线过滤重置分页且字面条件进入请求，数字按角色 ID 查', async () => {
  populated(); open(); await screen.findAllByText('角色甲'); fireEvent.click(screen.getByTitle('下一页'));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('characters?page=2&pageSize=20'));
  fireEvent.change(await search(), { target: { value: '中文%_' } });
  fireEvent.click(screen.getByLabelText('仅在线'));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('characters?page=1&pageSize=20&q=%E4%B8%AD%E6%96%87%25_&onlineOnly=true'));
  fireEvent.change(await search(), { target: { value: '42' } }); fireEvent.click(screen.getByRole('button', { name: /查\s*询/ }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('characters?page=1&pageSize=20&characterId=42&onlineOnly=true'));
  fireEvent.change(await search(), { target: { value: '0' } }); fireEvent.click(screen.getByRole('button', { name: /查\s*询/ }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('characters?page=1&pageSize=20&q=0&onlineOnly=true'));
});
it('详情按稳定ID关联账号和所属角色列表并支持刷新', async () => {
  populated(); open(); const panel = await detailPanel(); await within(panel).findByText('姓名编码');
  expect(within(panel).getByText('2,147,483,647')).toBeTruthy();
  await clickWhenIdle(() => within(panel).getByRole('button', { name: '刷新详情' }));
  await waitFor(() => expect(mocked.mock.calls.filter(([path]) => path === 'characters/1').length).toBe(2));
  fireEvent.click(within(panel).getByRole('button', { name: /查看归属账号/ }));
  const account = await screen.findByRole('region', { name: '账号详情' });
  await within(account).findByRole('button', { name: /查看该账号角色/ }); expect(mocked).toHaveBeenCalledWith('accounts/7');
  fireEvent.click(within(account).getByRole('button', { name: /查看该账号角色/ }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('characters?page=1&pageSize=20&accountId=7'));
  expect(screen.getByText('账号 #7 的角色')).toBeTruthy();
  await clickWhenIdle(() => screen.getByRole('button', { name: /刷新列表/ }));
  await waitFor(() => expect(mocked.mock.calls.filter(([path]) => path === 'characters?page=1&pageSize=20&accountId=7').length).toBe(2));
});
it('账号切换及登录名搜索使用账号分页契约', async () => {
  open(); await search(); fireEvent.click(screen.getByRole('radio', { name: '账号' }));
  fireEvent.change(await screen.findByLabelText('账号搜索'), { target: { value: 'player%' } });
  fireEvent.click(screen.getByRole('button', { name: /查\s*询/ }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('accounts?page=1&pageSize=20&q=player%25'));
  expect(await screen.findByText('没找到，换个关键词试试')).toBeTruthy();
});
it.each([[400, '查询参数无效。'], [500, '查询失败，请稍后重试。'], [0, '网络断开']])('查询失败%s明确反馈并清除旧数据', async (status, message) => {
  populated(); let failed = false; const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => { if (failed && path.startsWith('characters?')) throw status ? new ApiError(status, message) : new Error(message); return original(path); });
  open(); await screen.findAllByText('角色甲'); failed = true; await clickWhenIdle(() => screen.getByRole('button', { name: /刷新列表/ }));
  expect(await screen.findByText(message)).toBeTruthy();
  await waitFor(() => expect(screen.queryByRole('cell', { name: /角色甲/ })).toBeNull());
});
it('详情不存在显示404说明，查询会话失效回登录页', async () => {
  populated(); const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => { if (path === 'characters/1') throw new ApiError(404, '角色不存在。'); return original(path); });
  open(); expect((await screen.findAllByText('角色不存在。')).length).toBeGreaterThan(0);
  mocked.mockRejectedValue(new ApiError(401, '会话失效')); await clickWhenIdle(() => screen.getByRole('button', { name: /刷新列表/ }));
  expect(await screen.findByLabelText('管理员账号')).toBeTruthy(); expect(screen.queryByText('角色甲')).toBeNull();
});
it('快速连续查询的旧成功响应不能覆盖新结果', async () => {
  const old = deferred<unknown>(); const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => {
    if (path === 'characters?page=1&pageSize=20') return old.promise;
    if (path.includes('q=')) return { items: [{ ...character, name: '新角色' }], total: 1, page: 1, pageSize: 20, sampledAt: character.sampledAt };
    return original(path);
  });
  open(); fireEvent.change(await search(), { target: { value: '新' } }); fireEvent.click(screen.getByRole('button', { name: /查\s*询/ }));
  await screen.findAllByText('新角色'); await act(async () => { old.resolve({ items: [{ ...character, name: '旧角色' }], total: 1, page: 1, pageSize: 20, sampledAt: character.sampledAt }); });
  expect(screen.getAllByText('新角色').length).toBeGreaterThan(0); expect(screen.queryByText('旧角色')).toBeNull();
});
it('退出后的旧列表响应不能恢复私有资料', async () => {
  const pending = deferred<unknown>(); const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => path.startsWith('characters?') ? pending.promise : original(path));
  open(); await search(); await logout(); await screen.findByLabelText('管理员账号');
  await act(async () => { pending.resolve({ items: [character], total: 1, page: 1, pageSize: 20, sampledAt: character.sampledAt }); });
  expect(screen.queryByText('角色甲')).toBeNull(); expect(screen.getByLabelText('管理员账号')).toBeTruthy();
});
it('概览展示游戏关闭与真实人数并可刷新', async () => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => path === 'overview' ? { networkEnabled: false, gameReady: false, totalAccounts: 45, totalCharacters: 47,
    onlineAccounts: 2, onlineCharacters: 1, channels: [{ channelId: 11, name: '频道甲', onlineCharacters: 1 }], unmappedOnlineCharacters: 0, sampledAt: character.sampledAt } : original(path));
  open('/'); await screen.findByText('游戏服务未就绪'); expect(screen.getAllByText('游戏网络已关闭').length).toBe(2);
  expect(screen.getByText('频道甲')).toBeTruthy(); expect(screen.getByText('47')).toBeTruthy(); expect(screen.getByText(/采样 /)).toBeTruthy();
  expect(screen.getByLabelText('服务状态').textContent).toContain('游戏网络已关闭');
  await waitFor(() => expect(screen.getByRole('button', { name: /刷新概览/ }).className).not.toContain('loading'));
  await waitFor(() => expect(mocked.mock.calls.filter(([path]) => path === 'overview').length).toBe(2));
  const before = mocked.mock.calls.filter(([path]) => path === 'overview').length;
  fireEvent.click(screen.getByRole('button', { name: /刷新概览/ })); await waitFor(() => expect(mocked.mock.calls.filter(([path]) => path === 'overview').length).toBe(before + 1));
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
    open(); await screen.findAllByText('角色甲');
    await waitFor(() => expect(mocked).toHaveBeenCalledWith('characters/1')); revoked = true;
    await act(async () => { tick!(); }); await screen.findByText('没有访问此页面的权限');
    await act(async () => { pending.resolve(detail); });
    expect(screen.queryByText('角色甲')).toBeNull(); expect(screen.queryByText('姓名编码')).toBeNull();
    expect(screen.getByRole('button', { name: '账号与会话' })).toBeTruthy();
  } finally { interval.mockRestore(); }
});
it('姓名中的 HTML 按文本显示', async () => {
  populated(); const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => path.startsWith('characters?') ? { items: [{ ...character, name: '<img src=x onerror=alert(1)>' }], total: 1, page: 1, pageSize: 20, sampledAt: character.sampledAt } : original(path));
  open(); await screen.findAllByText('<img src=x onerror=alert(1)>'); expect(document.querySelector('img[src="x"]')).toBeNull();
});
it('403后的旧身份轮询成功不能恢复查询权限', async () => {
  populated(); const stale = deferred<unknown>(); let sessionRequests = 0; let tick: (() => void) | undefined; let forbidden = false;
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => {
    if (path === 'auth/session' && sessionRequests++ > 0) return stale.promise;
    if (path === 'characters/1' && forbidden) throw new ApiError(403, '没有执行此操作的权限。');
    return original(path);
  });
  const interval = vi.spyOn(window, 'setInterval').mockImplementation((handler, delay) => { if (delay === 60000) tick = handler as () => void; return 456; });
  try {
    open(); const panel = await detailPanel(); await within(panel).findByText('姓名编码'); await act(async () => { tick!(); });
    forbidden = true; await clickWhenIdle(() => within(panel).getByRole('button', { name: '刷新详情' })); await screen.findByText('没有访问此页面的权限');
    await act(async () => { stale.resolve(identity); });
    expect(screen.getByText('没有访问此页面的权限')).toBeTruthy(); expect(screen.queryByText('角色甲')).toBeNull();
    expect(within(screen.getByRole('navigation', { name: '主导航' })).queryByText('账号与角色')).toBeNull();
  } finally { interval.mockRestore(); }
});
it('顶栏搜索跳到账号与角色并带上关键词', async () => {
  populated(); open('/');
  fireEvent.change(await screen.findByLabelText('全局搜索'), { target: { value: '角色甲' } });
  fireEvent.keyDown(screen.getByLabelText('全局搜索'), { key: 'Enter', code: 'Enter', keyCode: 13 });
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('characters?page=1&pageSize=20&q=%E8%A7%92%E8%89%B2%E7%94%B2'));
  expect(((await search()) as HTMLInputElement).value).toBe('角色甲');
});
