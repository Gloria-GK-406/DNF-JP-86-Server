import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { api, ApiError } from './api';
import type { ItemCatalogDetails, ItemCatalogEntry } from './items';
import type { PlayerMe } from './playerApi';
import { clickWhenIdle, refuse, renderPortal, stubPlayerFetch, type PlayerCall, type PlayerReply } from './test-utils';
vi.mock('./api', async original => ({ ...await original<typeof import('./api')>(), api: vi.fn() }));
const mocked = vi.mocked(api);
const player = { accountId: 1, loginName: 'player01', expiresAt: '2030-01-01T00:00:00Z' };
const zero = { ceraPerGrant: 0, ceraPerDay: 0, tokenCeraPerGrant: 0, tokenCeraPerDay: 0, mailGoldPerMail: 0, mailsPerDay: 0 };
const material: ItemCatalogEntry = { itemId: 3037, name: '无色小晶块', category: 'material', equipmentType: null, level: null, rarity: 1 };
const materialDetails: ItemCatalogDetails = { item: material, kind: 'stackable', defaultSealed: false, durability: 0, equipment: null };
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
let me: PlayerMe;
let handle: (call: PlayerCall) => PlayerReply | undefined;
let calls: PlayerCall[];
beforeEach(() => {
  me = { accountId: 1, loginName: 'player01', balances: { cera: 1000, tokenCera: 50 },
    characters: [{ characterId: 11, name: '角色一', job: 0, level: 70, isOnline: true }, { characterId: 12, name: '角色二', job: 3, level: 62, isOnline: false }],
    portal: { ceraEnabled: true, mailEnabled: true, limits: { ...zero }, today: { cera: 0, tokenCera: 0, mails: 0 } } };
  handle = () => undefined;
  mocked.mockImplementation(async path => { if (path === 'auth/status') return { initialized: true }; if (path === 'auth/session') throw new ApiError(401, '请先登录'); return undefined; });
  calls = stubPlayerFetch(call => {
    const custom = handle(call); if (custom) return custom;
    if (call.path === 'auth/status') return { body: { enabled: true } };
    if (call.path === 'auth/session') return { body: player };
    if (call.path === 'me') return { body: me };
    if (call.path === 'items/icon-pack') return { body: { available: true } };
    if (call.path.startsWith('mail/items?')) return { body: { items: [material], page: 1, pageSize: 30, hasMore: false } };
    if (call.path === 'mail/items/3037') return { body: materialDetails };
    if (call.path.startsWith('items/')) return { body: null };
    if (call.path.startsWith('operations?')) return { body: { items: [{ requestId: 'r-1', kind: 'mail', committedAt: '2026-11-13T02:00:00Z', summary: '→ 角色二：无色小晶块×2', result: '成功' }], total: 1, page: 1, pageSize: 20 } };
    return undefined;
  });
});
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });
const grants = () => calls.filter(call => call.path === 'cera/grant');
const sends = () => calls.filter(call => call.path === 'mail/send');
const receipt = (body: { currency: string; amount: number }) => {
  const after = { ...me.balances, [body.currency === 'Cera' ? 'cera' : 'tokenCera']: me.balances[body.currency === 'Cera' ? 'cera' : 'tokenCera'] + body.amount };
  return { requestId: 'x', kind: 'cera', committedAt: '2026-11-13T00:00:00Z', summary: '', result: '成功', currency: body.currency, amount: body.amount, before: me.balances, after };
};
async function open(path = '/player') { renderPortal(path); await screen.findByRole('navigation', { name: '玩家导航' }); }
const confirm = () => screen.getByRole('button', { name: /确\s*认/ }) as HTMLButtonElement;
// A loading antd button adds its spinner to the accessible name; match by substring and wait until idle.
const retry = () => screen.getByRole('button', { name: /用原编号重试/ });

describe('我的账号与加点券', () => {
  it('快捷加数与币种切换更新加后预览，提交带新编号，提醒失败仍按成功显示', async () => {
    handle = call => { if (call.path !== 'cera/grant') return undefined;
      const body = call.body as { currency: string; amount: number }; const value = receipt(body); me = { ...me, balances: value.after };
      return { body: { receipt: value, replayed: false, reminder: 'failed' } }; };
    await open();
    expect(await screen.findByText('1,000')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: '+1 万' })); fireEvent.click(screen.getByRole('button', { name: '+10 万' }));
    expect(screen.getByLabelText('加后').textContent).toBe('111,000');
    fireEvent.click(screen.getByText('代币券', { selector: '.ant-segmented-item-label' }));
    expect(screen.getByLabelText('加后').textContent).toBe('110,050');
    fireEvent.click(confirm());
    expect(await screen.findByText('代币券 +110,000')).toBeTruthy();
    expect(grants()).toHaveLength(1);
    expect(Object.keys(grants()[0].body as object)).toEqual(['requestId', 'currency', 'amount']);
    expect(grants()[0].body).toMatchObject({ currency: 'TokenCera', amount: 110000, requestId: expect.stringMatching(uuid) });
    expect(grants()[0].csrf).toBe('player-token');
    expect(await screen.findByText('110,050')).toBeTruthy();
  });
  it('结果未确认时锁定表单，用原编号重试；明确被拒后下一次换新编号', async () => {
    const replies: (() => PlayerReply)[] = [
      () => ({ status: 500, body: 'Internal Server Error' }),
      () => { throw new TypeError('Failed to fetch'); },
      () => refuse(400, 'invalid', '数量不合法。', 'rejected'),
      () => ({ body: { receipt: receipt({ currency: 'Cera', amount: 5 }), replayed: true, reminder: 'offline' } }),
    ];
    handle = call => call.path === 'cera/grant' ? replies.shift()!() : undefined;
    await open();
    fireEvent.change(await screen.findByLabelText('数量'), { target: { value: '5' } });
    fireEvent.click(confirm());
    expect(await screen.findByText('结果未确认')).toBeTruthy();
    expect(confirm().disabled).toBe(true); expect((screen.getByLabelText('数量') as HTMLInputElement).disabled).toBe(true);
    await clickWhenIdle(retry);
    await waitFor(() => expect(grants()).toHaveLength(2));
    expect(await screen.findByText('结果未确认')).toBeTruthy();
    expect(grants()[1].body).toEqual(grants()[0].body);
    await clickWhenIdle(retry);
    expect(await screen.findByText('数量不合法。')).toBeTruthy();
    expect(grants()[2].body).toEqual(grants()[0].body);
    await waitFor(() => expect(screen.queryByText('结果未确认')).toBeNull());
    await clickWhenIdle(confirm);
    expect(await screen.findByText('点券 +5')).toBeTruthy();
    expect((grants()[3].body as { requestId: string }).requestId).not.toBe((grants()[0].body as { requestId: string }).requestId);
  });
  it('结果未确认后切到别的页再回来仍锁定，重试用的仍是原编号', async () => {
    const replies: (() => PlayerReply)[] = [() => ({ status: 502, body: 'Bad Gateway' }),
      () => ({ body: { receipt: receipt({ currency: 'Cera', amount: 7 }), replayed: false, reminder: 'attempted' } })];
    handle = call => call.path === 'cera/grant' ? replies.shift()!() : undefined;
    await open();
    fireEvent.change(await screen.findByLabelText('数量'), { target: { value: '7' } });
    fireEvent.click(confirm());
    expect(await screen.findByText('结果未确认')).toBeTruthy();
    const nav = screen.getByRole('navigation', { name: '玩家导航' });
    fireEvent.click(within(nav).getByText('记录')); await screen.findByText('→ 角色二：无色小晶块×2');
    expect(screen.queryByText('结果未确认')).toBeNull();
    fireEvent.click(within(nav).getByText('我的账号'));
    expect(await screen.findByText('结果未确认')).toBeTruthy();
    expect(confirm().disabled).toBe(true); expect((screen.getByLabelText('数量') as HTMLInputElement).value).toBe('7');
    await clickWhenIdle(retry);
    expect(await screen.findByText('点券 +7')).toBeTruthy();
    expect(grants()).toHaveLength(2); expect(grants()[1].body).toEqual(grants()[0].body);
  });
  it('首次读取账号失败时显示错误与"重新读取"，不一直转圈', async () => {
    let fail = true;
    handle = call => call.path === 'me' && fail ? { status: 500, body: 'Internal Server Error' } : undefined;
    renderPortal('/player');
    expect(await screen.findByText('服务暂不可用，请稍后重试。')).toBeTruthy();
    expect(screen.getByText('HTTP 500')).toBeTruthy();
    fail = false; fireEvent.click(screen.getByRole('button', { name: /重新读取/ }));
    expect(await screen.findByText('1,000')).toBeTruthy();
    expect(screen.queryByText('服务暂不可用，请稍后重试。')).toBeNull();
  });
  it('点券子开关关闭时表单不可提交', async () => {
    me.portal.ceraEnabled = false; await open();
    await screen.findByText('已停用');
    expect(confirm().disabled).toBe(true); expect((screen.getByRole('button', { name: '+1 万' }) as HTMLButtonElement).disabled).toBe(true);
  });
  it('提交时点券子开关刚被关闭：提示服务端消息并重读为已停用', async () => {
    handle = call => { if (call.path !== 'cera/grant') return undefined; me = { ...me, portal: { ...me.portal, ceraEnabled: false } }; return refuse(403, 'feature_disabled', '加点券已关闭。', 'rejected'); };
    await open();
    fireEvent.change(await screen.findByLabelText('数量'), { target: { value: '5' } });
    fireEvent.click(confirm());
    expect(await screen.findByText('加点券已关闭。')).toBeTruthy();
    expect(await screen.findByText('已停用')).toBeTruthy();
    expect(screen.queryByText('玩家后台已关闭')).toBeNull();
  });
  it('设置了每日上限时显示今日用量，并按单次与剩余额度拦截', async () => {
    me.portal.limits = { ...zero, ceraPerGrant: 1000, ceraPerDay: 1500 }; me.portal.today.cera = 1000; await open();
    expect((await screen.findByLabelText('今日用量')).textContent).toContain('1,000 / 1,500');
    fireEvent.change(screen.getByLabelText('数量'), { target: { value: '1001' } });
    expect(await screen.findByText('单次上限 1,000')).toBeTruthy(); expect(confirm().disabled).toBe(true);
    fireEvent.change(screen.getByLabelText('数量'), { target: { value: '501' } });
    expect(await screen.findByText('今日剩余 500')).toBeTruthy();
  });
  it('每 60 秒重读账号，玩家后台被关闭后停在页面上的玩家进入已关闭页', async () => {
    let tick: (() => void) | undefined;
    const interval = vi.spyOn(window, 'setInterval').mockImplementation((handler, delay) => { if (delay === 60000) tick = handler as () => void; return 4321; });
    try {
      await open(); await screen.findByText('1,000');
      handle = call => call.path === 'me' ? refuse(403, 'portal_closed', '玩家后台已关闭。') : call.path === 'auth/status' ? { body: { enabled: false } } : undefined;
      await act(async () => { tick!(); });
      expect(await screen.findByText('玩家后台已关闭')).toBeTruthy();
    } finally { interval.mockRestore(); }
  });
});

describe('发邮件', () => {
  it('收件人只列自己的角色；物品检索与图标走玩家前缀；请求只带六个字段', async () => {
    handle = call => call.path === 'mail/send' ? { body: { receipt: { requestId: 'r', kind: 'mail', committedAt: '2026-11-13T00:00:00Z', summary: '', result: '成功', characterName: '角色二' }, replayed: false } } : undefined;
    await open();
    fireEvent.click(await screen.findByRole('button', { name: '给 角色二 发邮件' }));
    const group = await screen.findByRole('radiogroup', { name: '收件角色' });
    expect(within(group).getAllByRole('radio').map(radio => radio.textContent)).toEqual(['鬼角色一Lv.70 · 在线', '魔角色二Lv.62']);
    expect(within(group).getByRole('radio', { checked: true }).textContent).toContain('角色二');
    fireEvent.change(screen.getByLabelText('物品搜索'), { target: { value: '晶块' } });
    const add = await screen.findByRole('button', { name: '添加物品 3037' });
    expect(calls.some(call => call.path.startsWith('mail/items?') && call.path.includes('q=%E6%99%B6%E5%9D%97'))).toBe(true);
    fireEvent.click(add); fireEvent.click(await screen.findByRole('button', { name: '添加物品 3037' }));
    expect(await screen.findByText('已加 2')).toBeTruthy();
    await waitFor(() => expect(calls.some(call => call.path === 'mail/items/3037')).toBe(true));
    await waitFor(() => expect(document.querySelector('img[src="/api/player/v1/items/3037/icon"]')).not.toBeNull());
    expect(screen.queryByText('品级')).toBeNull();
    fireEvent.change(screen.getByLabelText('金币'), { target: { value: '500' } });
    fireEvent.change(screen.getByLabelText('正文'), { target: { value: '你好' } });
    fireEvent.click(screen.getByRole('button', { name: /发\s*送/ }));
    expect(await screen.findByText('已送达')).toBeTruthy();
    expect(sends()).toHaveLength(1);
    expect(Object.keys(sends()[0].body as object)).toEqual(['requestId', 'characterId', 'title', 'body', 'items', 'gold']);
    expect(sends()[0].body).toMatchObject({ characterId: 12, title: '', body: '你好', items: [{ itemId: 3037, count: 2 }], gold: 500, requestId: expect.stringMatching(uuid) });
    expect(mocked.mock.calls.every(([path]) => path === 'auth/status' || path === 'auth/session')).toBe(true);
  });
  it('未选收件人或内容为空时不提交', async () => {
    await open('/player/mail');
    fireEvent.click(await screen.findByRole('button', { name: /发\s*送/ }));
    expect(await screen.findByText('请选择收件角色')).toBeTruthy();
    fireEvent.click(screen.getByRole('radio', { name: /角色一/ }));
    expect(await screen.findByText('正文、附件、金币至少填一项')).toBeTruthy();
    expect(sends()).toHaveLength(0);
  });
  it('邮件子开关关闭时表单不可提交，也不请求物品目录、图标与提示', async () => {
    me.portal.mailEnabled = false; await open();
    expect((await screen.findByRole('button', { name: '给 角色一 发邮件' }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(within(screen.getByRole('navigation', { name: '底部导航' })).getByText('发邮件'));
    expect(await screen.findByText('已停用')).toBeTruthy();
    expect((screen.getByRole('button', { name: /发\s*送/ }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.queryByLabelText('物品搜索')).toBeNull();
    expect(calls.some(call => call.path.startsWith('mail/items') || call.path.startsWith('items/'))).toBe(false);
  });
});

describe('记录', () => {
  it('按类型筛选并显示回执摘要与结果', async () => {
    await open();
    fireEvent.click(within(screen.getByRole('navigation', { name: '玩家导航' })).getByText('记录'));
    expect(await screen.findByText('→ 角色二：无色小晶块×2')).toBeTruthy();
    expect(calls.some(call => call.path === 'operations?page=1&pageSize=20')).toBe(true);
    fireEvent.click(screen.getByText('邮件', { selector: '.ant-segmented-item-label' }));
    await waitFor(() => expect(calls.some(call => call.path === 'operations?page=1&pageSize=20&kind=mail')).toBe(true));
    expect(screen.getAllByText('成功').length).toBeGreaterThan(0);
  });
});
