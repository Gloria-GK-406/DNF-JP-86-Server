import { fireEvent, render, screen, waitFor, act, within } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import zhCN from 'antd/locale/zh_CN';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import { AdminApp } from './AdminApp';
import { api, ApiError } from './api';
import type { CeraInput, PendingCera } from './cera';
vi.mock('./api', async original => ({ ...await original<typeof import('./api')>(), api: vi.fn() }));
const mocked = vi.mocked(api);
const identity = { id: 1, loginName: '点券管理员', permissions: ['GrantCera'], expiresAt: '2030-01-01T00:00:00Z' };
const balances = { cera: 10, tokenCera: 20, happyTokenCera: 30 };
const input: CeraInput = { accountIds: [1], currency: 'Cera', amount: 1, reason: '补偿' };
const account = { accountId: 1, loginName: '无角色账号', balances };
const changes = [{ accountId: 1, loginName: account.loginName, before: balances, after: { ...balances, cera: 11 } }];
const receipt = (id: string, requested = input) => ({ requestId: id, administratorId: 1, administratorName: identity.loginName, committedAt: '2026-10-02T00:00:00Z', input: requested, accounts: changes });
const key = 'dfo.gm.cera.pending.1';
const pending: PendingCera = { requestId: '12345678-1234-4234-8234-123456789abc', input };
function open() { return render(<ConfigProvider locale={zhCN} theme={{ token: { motion: false } }}><App><MemoryRouter initialEntries={['/wallet']}><AdminApp /></MemoryRouter></App></ConfigProvider>); }
async function editor() {
  fireEvent.click(await screen.findByRole('button', { name: '发放点券' })); await screen.findByLabelText('发放原因');
}
async function accountsStep() {
  fireEvent.click(screen.getByRole('button', { name: '下一步：选择账号' })); await screen.findByLabelText('选择账号 1');
}
async function pendingWindow() {
  fireEvent.click(await screen.findByRole('button', { name: '待确认发放（1）' })); await screen.findByRole('dialog', { name: '发放结果待确认' });
}
async function resultDetails() {
  await waitFor(() => expect(screen.queryByRole('dialog', { name: '发放结果待确认' })).toBeNull());
  fireEvent.click(await screen.findByRole('button', { name: '查看余额详情' })); await screen.findByRole('dialog', { name: '点券发放记录详情' });
}
async function preview() {
  await editor();
  fireEvent.change(await screen.findByLabelText('发放原因'), { target: { value: '补偿' } });
  await accountsStep();
  fireEvent.click(await screen.findByLabelText('选择账号 1')); fireEvent.click(screen.getByRole('button', { name: '预览并核对' }));
  await screen.findByRole('button', { name: '确认整批发放' });
}
beforeEach(() => {
  let delivered: ReturnType<typeof receipt> | undefined;
  sessionStorage.clear(); mocked.mockImplementation(async (path, payload) => {
    if (path === 'auth/status') return { initialized: true }; if (path === 'auth/session') return identity;
    if (path.startsWith('cera/accounts?')) return { items: [account], total: 1, page: 1, pageSize: 20 };
    if (path.startsWith('cera/operations?')) return { items: delivered ? [delivered] : [], total: delivered ? 1 : 0, page: 1, pageSize: 20 };
    if (path === 'cera/preview') return { input: payload, accounts: changes, totalAmount: (payload as CeraInput).amount };
    if (path === 'cera/grant') { delivered = receipt((payload as PendingCera).requestId, payload as CeraInput); return { receipt: delivered, replayed: false, reminder: 'unconfirmed' }; }
    throw new Error('Unexpected path ' + path);
  });
});
it('点券页默认只展示发放记录，点击发放后才打开编辑窗口', async () => {
  open(); await screen.findByText('我的点券发放记录');
  expect(screen.queryByLabelText('发放原因')).toBeNull();
  expect(mocked.mock.calls.some(([path]) => path.startsWith('cera/accounts?'))).toBe(false);
  fireEvent.click(screen.getByRole('button', { name: '发放点券' }));
  expect(await screen.findByRole('dialog', { name: '发放点券' })).toBeTruthy();
  expect(screen.getByLabelText('发放原因')).toBeTruthy();
});
it('GrantCera独立权限选择无角色账号，预览确认和结果记录可用', async () => {
  open(); await preview(); expect(screen.getByText('提交按当前余额结算，以发放结果为准。')).toBeTruthy();
  expect(screen.queryByLabelText('发放原因')).toBeNull(); expect(screen.getAllByRole('dialog')).toHaveLength(1);
  fireEvent.click(screen.getByRole('button', { name: '确认整批发放' })); await screen.findByText('整批点券已到账。');
  await waitFor(() => expect(screen.queryByRole('dialog', { name: '发放点券' })).toBeNull());
  expect(screen.queryByText('持久化发放结果')).toBeNull(); await resultDetails();
  expect(await screen.findByText('持久化发放结果')).toBeTruthy();
  expect(mocked).toHaveBeenCalledWith('cera/grant', expect.objectContaining({ ...input, requestId: expect.any(String) }));
  expect(sessionStorage.getItem(key)).toBeNull(); expect(screen.getByText(/已到账，在线提醒未确认/)).toBeTruthy();
  expect(mocked.mock.calls.some(([p]) => p.startsWith('characters') || p.startsWith('accounts?') || p.startsWith('audit'))).toBe(false);
});
it('明确批量和币种编辑仅提交预览冻结输入', async () => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => path.startsWith('cera/accounts?') ? { items: [account, { ...account, accountId: 2, loginName: '账号二' }], total: 2 } : original(path, payload));
  open(); await editor(); fireEvent.change(await screen.findByLabelText('币种'), { target: { value: 'HappyTokenCera' } });
  fireEvent.change(screen.getByLabelText('每账号增量'), { target: { value: '50' } }); fireEvent.change(screen.getByLabelText('发放原因'), { target: { value: '批量补偿' } });
  await accountsStep(); fireEvent.click(await screen.findByLabelText('选择账号 1')); fireEvent.click(screen.getByLabelText('选择账号 2')); fireEvent.click(screen.getByRole('button', { name: '预览并核对' }));
  fireEvent.click(await screen.findByRole('button', { name: '确认整批发放' })); await screen.findByText('整批点券已到账。'); await resultDetails();
  expect(mocked).toHaveBeenCalledWith('cera/grant', expect.objectContaining({ accountIds: [1,2], currency: 'HappyTokenCera', amount: 50, reason: '批量补偿' }));
});
it('sessionStorage写入失败在发送前停止', async () => {
  open(); await preview(); const storage = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota'); });
  fireEvent.click(screen.getByRole('button', { name: '确认整批发放' })); expect(await screen.findByText('浏览器无法保存待确认请求，请启用会话存储后重试。')).toBeTruthy();
  expect(mocked.mock.calls.some(([p]) => p === 'cera/grant')).toBe(false); storage.mockRestore();
});
it('超时冻结UUID，刷新不自动发放，原内容安全重试恢复一次结果', async () => {
  const original = mocked.getMockImplementation()!; let first = true;
  mocked.mockImplementation(async (path, payload) => { if (path === 'cera/grant' && first) { first = false; throw new Error('timeout'); } return original(path, payload); });
  const view = open(); await preview(); fireEvent.click(screen.getByRole('button', { name: '确认整批发放' })); await screen.findByRole('button', { name: '待确认发放（1）' });
  const saved = JSON.parse(sessionStorage.getItem(key)!) as PendingCera; view.unmount(); open(); await screen.findByRole('button', { name: '待确认发放（1）' });
  expect(screen.queryByRole('dialog')).toBeNull(); expect((screen.getByRole('button', { name: '发放点券' }) as HTMLButtonElement).disabled).toBe(true);
  expect(mocked.mock.calls.filter(([p]) => p === 'cera/grant')).toHaveLength(1);
  await pendingWindow(); fireEvent.click(screen.getByRole('button', { name: '按原内容安全重试' })); await screen.findByText('整批点券已到账。'); await resultDetails();
  const calls = mocked.mock.calls.filter(([p]) => p === 'cera/grant'); expect(calls).toHaveLength(2); expect(calls[1][1]).toEqual({ ...saved.input, requestId: saved.requestId });
});
it('未知结果查询404保留冻结请求，后续查询恢复持久结果', async () => {
  sessionStorage.setItem(key, JSON.stringify(pending)); const original = mocked.getMockImplementation()!; let found = false;
  mocked.mockImplementation(async (path, payload) => { if (path === `cera/operations/${pending.requestId}`) { if (!found) throw new ApiError(404, '未找到已提交记录，请保留原编号'); return receipt(pending.requestId); } return original(path,payload); });
  open(); await pendingWindow(); fireEvent.click(await screen.findByRole('button', { name: '查询原编号结果' })); await screen.findByText('未找到已提交记录，请保留原编号');
  expect(sessionStorage.getItem(key)).not.toBeNull(); found = true; fireEvent.click(screen.getByRole('button', { name: '查询原编号结果' }));
  await screen.findByText('持久化发放结果'); expect(sessionStorage.getItem(key)).toBeNull(); expect(mocked.mock.calls.some(([p]) => p === 'cera/grant')).toBe(false);
});
it.each([[400,'rejected'],[409,'rolledBack'],[500,'rolledBack']])('明确错误%s释放冻结内容供重新核对', async (status,outcome) => {
  const original = mocked.getMockImplementation()!; mocked.mockImplementation(async (p,v) => { if (p === 'cera/grant') throw new ApiError(status,'明确未提交',outcome); return original(p,v); });
  open(); await preview(); fireEvent.click(screen.getByRole('button',{name:'确认整批发放'})); await screen.findByText('明确未提交');
  expect(sessionStorage.getItem(key)).toBeNull(); expect((screen.getByLabelText('发放原因') as HTMLTextAreaElement).disabled).toBe(false);
});
it.each([401,403])('身份或权限%s失效清除页面资料且迟到响应不能恢复', async status => {
  const original = mocked.getMockImplementation()!; let complete!: (x: unknown) => void;
  mocked.mockImplementation(async (p,v) => { if(p === 'cera/preview') return new Promise(r => { complete = r; }); if(p === 'cera/accounts?page=1&pageSize=20' && mocked.mock.calls.filter(([x])=>x===p).length>1) throw new ApiError(status,'失权'); return original(p,v); });
  open(); await editor(); fireEvent.change(await screen.findByLabelText('发放原因'),{target:{value:'补偿'}}); await accountsStep(); fireEvent.click(await screen.findByLabelText('选择账号 1'));
  fireEvent.click(screen.getByRole('button',{name:'预览并核对'}));
  // The independent record read can revoke this mounted page while a write response is pending.
  mocked.mockImplementation(async(p,v)=>{ if(p.startsWith('cera/operations?'))throw new ApiError(status,'失权');return original(p,v); });
  fireEvent.click(screen.getByRole('button',{name:'刷新记录'}));
  await screen.findByText(status===401?'会话已失效，请重新登录。':'没有访问此页面的权限');
  await act(async()=>complete({input,accounts:changes,totalAmount:1}));
  expect(screen.queryByRole('dialog', { name: '发放点券' })).toBeNull();expect(screen.queryByText('无角色账号')).toBeNull();
});
it.each([401,403])('提交身份失效%s立即隐藏冻结内容和余额', async status => {
  const original=mocked.getMockImplementation()!;mocked.mockImplementation(async(p,v)=>{if(p==='cera/grant')throw new ApiError(status,'失权','unknown');return original(p,v);});
  open();await preview();fireEvent.click(screen.getByRole('button',{name:'确认整批发放'}));
  await screen.findByText(status===401?'会话已失效，请重新登录。':'没有访问此页面的权限');
  expect(screen.queryByText('无角色账号')).toBeNull();expect(screen.queryByText('发放结果待确认')).toBeNull();expect(screen.queryByLabelText('发放原因')).toBeNull();
});
it('跨页明确选择并可取消不在当前页的账号', async()=>{
  const original=mocked.getMockImplementation()!;
  mocked.mockImplementation(async(p,v)=>p.startsWith('cera/accounts?')?{items:[p.includes('page=2&')?{...account,accountId:21,loginName:'第二页账号'}:account],total:21,page:p.includes('page=2&')?2:1,pageSize:20}:original(p,v));
  open(); await editor(); await accountsStep(); fireEvent.click(await screen.findByLabelText('选择账号 1'));const buttons=screen.getAllByTitle('2');fireEvent.click(buttons[buttons.length-1]);
  fireEvent.click(await screen.findByLabelText('选择账号 21'));expect(screen.getByText('已选择 2 个不同账号（最多 100）：1、21')).toBeTruthy();
  fireEvent.click(screen.getByRole('button',{name:'取消账号 1'}));expect(screen.getByText('已选择 1 个不同账号（最多 100）：21')).toBeTruthy();
});

it('关闭未提交编辑可继续修改，放弃后重新发放从空白开始', async () => {
  open(); await editor();
  fireEvent.change(screen.getByLabelText('发放原因'), { target: { value: '未提交内容' } });
  fireEvent.click(screen.getByRole('button', { name: '取消编辑' }));
  fireEvent.click(await screen.findByRole('button', { name: '继续编辑' }));
  expect((screen.getByLabelText('发放原因') as HTMLTextAreaElement).value).toBe('未提交内容');
  await accountsStep(); fireEvent.click(screen.getByLabelText('选择账号 1'));
  fireEvent.click(screen.getByRole('button', { name: '取消编辑' }));
  fireEvent.click(await screen.findByRole('button', { name: '放弃修改' }));
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  await editor(); expect((screen.getByLabelText('发放原因') as HTMLTextAreaElement).value).toBe('');
  await accountsStep(); expect((screen.getByLabelText('选择账号 1') as HTMLInputElement).checked).toBe(false);
  expect(mocked.mock.calls.some(([path]) => path === 'cera/grant')).toBe(false);
});

it('预览后返回修改保留账号选择，重新核对仅发放新的预览内容', async () => {
  open(); await preview();
  fireEvent.click(screen.getByRole('button', { name: '返回上一步' }));
  expect((await screen.findByLabelText('选择账号 1') as HTMLInputElement).checked).toBe(true);
  fireEvent.click(screen.getByRole('button', { name: '返回上一步' }));
  fireEvent.change(screen.getByLabelText('每账号增量'), { target: { value: '25' } });
  fireEvent.change(screen.getByLabelText('发放原因'), { target: { value: '修改后的补偿' } });
  await accountsStep(); expect((screen.getByLabelText('选择账号 1') as HTMLInputElement).checked).toBe(true);
  fireEvent.click(screen.getByRole('button', { name: '预览并核对' }));
  fireEvent.click(await screen.findByRole('button', { name: '确认整批发放' })); await screen.findByText('整批点券已到账。');
  expect(mocked.mock.calls.filter(([path]) => path === 'cera/grant')).toHaveLength(1);
  expect(mocked).toHaveBeenCalledWith('cera/grant', expect.objectContaining({ amount: 25, reason: '修改后的补偿', accountIds: [1] }));
});

it('恢复的待确认请求明确拒绝后回到编辑并保留原发放内容', async () => {
  sessionStorage.setItem(key, JSON.stringify({ ...pending, input: { ...input, amount: 88, currency: 'TokenCera' } }));
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => { if (path === 'cera/grant') throw new ApiError(409, '整批未提交', 'rejected'); return original(path, payload); });
  open(); await pendingWindow(); fireEvent.click(screen.getByRole('button', { name: '按原内容安全重试' }));
  await screen.findByText('整批未提交');
  expect(sessionStorage.getItem(key)).toBeNull();
  const dialog = await screen.findByRole('dialog', { name: '发放点券' });
  expect((within(dialog).getByLabelText('每账号增量') as HTMLInputElement).value).toBe('88');
  expect((within(dialog).getByLabelText('币种') as HTMLSelectElement).value).toBe('TokenCera');
  expect((within(dialog).getByLabelText('发放原因') as HTMLTextAreaElement).value).toBe('补偿');
  await accountsStep(); expect((screen.getByLabelText('选择账号 1') as HTMLInputElement).checked).toBe(true);
});

it('退出登录后的迟到发放响应不恢复详情，待确认请求保留用于下次查询', async () => {
  const original = mocked.getMockImplementation()!; let resolve!: (value: unknown) => void;
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'cera/grant') return new Promise(done => { resolve = done; });
    if (path === 'auth/logout') return undefined;
    return original(path, payload);
  });
  open(); await preview(); fireEvent.click(screen.getByRole('button', { name: '确认整批发放' }));
  const saved = JSON.parse(sessionStorage.getItem(key)!) as PendingCera;
  fireEvent.click(screen.getByRole('button', { name: '退出登录' })); await screen.findByRole('button', { name: /^登\s*录$/ });
  await act(async () => resolve({ receipt: receipt(saved.requestId), replayed: false, reminder: 'unconfirmed' }));
  expect(screen.queryByText('持久化发放结果')).toBeNull();
  expect(screen.queryByText('整批点券已到账。')).toBeNull();
  expect(sessionStorage.getItem(key)).not.toBeNull();
});
