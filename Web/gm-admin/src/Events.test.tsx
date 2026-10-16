import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { Modal } from 'antd';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { api, ApiError } from './api';
import type { EventEntry, EventList } from './EventsView';
import type { WeeklyAttendanceSettingsData } from './events/WeeklyAttendanceSettings';
import { renderApp } from './test-utils';

vi.mock('./api', async original => ({ ...await original<typeof import('./api')>(), api: vi.fn() }));
const mocked = vi.mocked(api);
const identity = { id: 1, loginName: '活动管理员', permissions: ['QueryPlayers', 'SendMail', 'GrantCera', 'ViewAudit'], expiresAt: '2030-01-01T00:00:00Z' };
let data: EventList;
let settings: WeeklyAttendanceSettingsData;
const writes = () => mocked.mock.calls.filter(([path, payload]) => path === 'events' && payload);
const open = () => renderApp('/events');
const rail = () => within(screen.getByRole('navigation', { name: '主导航' }));
const closeDrawer = (dialog: HTMLElement) => fireEvent.click(within(dialog).getAllByRole('button', { name: /关\s*闭/ }).at(-1)!);
async function edit() { fireEvent.click(await screen.findByRole('button', { name: '通用配置' })); return screen.findByRole('dialog'); }
const config = { title: '累计周签到', shortName: '周签到', description: '完成推荐地下城后签到', startNotice: '签到开启', endNotice: '签到结束',
  startsAt: '2026-10-01T00:00:00.000Z', endsAt: '2026-12-31T00:00:00.000Z' };
afterEach(() => Modal.destroyAll());
beforeEach(() => {
  data = { entries: [{ eventId: 2208, type: 'weekly-attendance', revision: 8, seasonId: 'season-one', enabled: false, status: 'disabled', ...config,
    available: true, unavailableReason: null, updatedAt: null, updatedBy: null }] };
  settings = { eventId: 2208, revision: 8, seasonId: 'season-one', sourcePath: 'event/chn_event/chn_totalattendance.evt', available: true,
    unavailableReason: null, requiredClears: 4, totalWeeks: 12, weeklyRewards: [{ attendanceNo: 1, itemId: 90001, itemName: '力量药剂', count: 2 }],
    cumulativeRewards: [{ attendanceNo: 4, itemId: 90004, itemName: '累计签到礼盒', count: 1 }] };
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'auth/status') return { initialized: true }; if (path === 'auth/session') return identity;
    if (path === 'events/weekly-attendance/settings') return structuredClone(settings);
    if (path === 'events') {
      if (payload) {
        const change = payload as Partial<EventEntry> & { action: string };
        const row = data.entries[0]; row.revision++;
        if (change.action === 'enable') { row.enabled = true; row.status = 'active'; }
        else if (change.action === 'disable') { row.enabled = false; row.status = 'disabled'; }
        else {
          Object.assign(row, { title: change.title, shortName: change.shortName, description: change.description, startNotice: change.startNotice,
            endNotice: change.endNotice, startsAt: change.startsAt, endsAt: change.endsAt });
          if (change.action === 'new-season') { row.seasonId = 'season-two'; row.enabled = false; row.status = 'disabled'; }
        }
      }
      return structuredClone(data);
    }
    if (path === 'server-info' || path === 'items/icon-pack' || path === 'overview') return undefined;
    throw new Error('Unexpected request: ' + path);
  });
});

it('活动列表和通用配置仅展示公共信息，不读取或展示专属规则', async () => {
  open(); expect(await screen.findByRole('button', { name: /活动管理/ })).toBeTruthy();
  expect(await screen.findByText('season-one')).toBeTruthy(); expect(screen.getByText('已停用')).toBeTruthy();
  expect(screen.queryByText(/event\/chn_event\/chn_totalattendance.evt/)).toBeNull();
  expect(screen.queryByText('规则与奖励')).toBeNull(); expect(screen.queryByText('每周奖励')).toBeNull();
  expect(mocked.mock.calls.some(([path]) => path === 'events/weekly-attendance/settings')).toBe(false);
  expect(screen.queryByRole('dialog')).toBeNull(); await edit();
  expect(screen.queryByLabelText(/奖励/)).toBeNull(); expect(screen.queryByLabelText(/签到目标/)).toBeNull();
});
it('设置活动独立读取周签到规则，显示PVF来源、目标和两类奖励且只读', async () => {
  open(); fireEvent.click(await screen.findByRole('button', { name: '设置活动' }));
  const dialog = await screen.findByRole('dialog');
  expect(await within(dialog).findByText('event/chn_event/chn_totalattendance.evt')).toBeTruthy();
  expect(within(dialog).getByText('PVF · 只读')).toBeTruthy(); expect(within(dialog).getByText('4 次推荐地下城')).toBeTruthy();
  expect(within(dialog).getByText('90001')).toBeTruthy(); expect(within(dialog).getByText('90004')).toBeTruthy();
  expect(within(dialog).getByRole('cell', { name: /力量药剂（\s*90001/ })).toBeTruthy();
  expect(within(dialog).getByRole('cell', { name: /累计签到礼盒（\s*90004/ })).toBeTruthy();
  expect(within(dialog).queryByRole('textbox')).toBeNull(); expect(within(dialog).queryByRole('spinbutton')).toBeNull();
  expect(within(dialog).queryByRole('button', { name: /保存/ })).toBeNull();
  closeDrawer(dialog);
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull()); expect(writes()).toHaveLength(0);
});
it('缺失名称保留ID并标明未找到，名称作为文本显示且不产生额外查询', async () => {
  settings.weeklyRewards[0].itemName = null; settings.cumulativeRewards[0].itemName = '<b>签到礼盒</b>';
  open(); fireEvent.click(await screen.findByRole('button', { name: '设置活动' }));
  const dialog = await screen.findByRole('dialog');
  expect(await within(dialog).findByRole('cell', { name: /名称未找到（\s*90001/ })).toBeTruthy();
  expect(within(dialog).getByRole('cell', { name: /<b>签到礼盒<\/b>（\s*90004/ })).toBeTruthy();
  expect(mocked.mock.calls.some(([path]) => /item|catalog/.test(path) && path !== 'items/icon-pack')).toBe(false);
});
it('未知活动类型只显示通用管理，不借用周签到界面', async () => {
  data.entries[0].type = 'future-event';
  open(); expect(await screen.findByText(/future-event/)).toBeTruthy(); expect(screen.getByRole('button', { name: '通用配置' })).toBeTruthy();
  expect(screen.queryByRole('button', { name: '设置活动' })).toBeNull();
});
it('马战设置独立分派，停用明确保留已有投注的结算责任', async () => {
  data.entries[0].type = 'joust'; data.entries[0].title = '骑士马战大竞猜'; data.entries[0].enabled = true;
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation((path, payload) => path === 'events/joust/settings' ? Promise.resolve({
    eventId: 2365, revision: 4, schedule: { dailyStartTime: '10:00', roundsPerDay: 7, roundIntervalMinutes: 120, bettingMinutes: 90, lockMinutes: 10, stageSeconds: 200 },
    parameters: { strongMultiplier: 1.15, normalMultiplier: 1, weakMultiplier: .85, counterMultiplier: 1.25, historyPrior: 10, forecastWeight: .4, bettingScale: 1000 },
    sourcePath: 'event/chn_event/chn_joust.evt', available: true, unavailableReason: null, minimumLevel: 17, maximumBetting: 1000,
    knights: [], currencies: [], reward: null, bettingReward: null, updatedAt: null, updatedBy: null,
  }) : original(path, payload));
  open(); fireEvent.click(await screen.findByRole('button', { name: '设置活动' }));
  const dialog = await screen.findByRole('dialog'); await within(dialog).findByText('event/chn_event/chn_joust.evt');
  expect(within(dialog).getByLabelText('强势战斗力倍率')).toBeTruthy();
  expect(mocked.mock.calls.some(([path]) => path === 'events/weekly-attendance/settings')).toBe(false);
  closeDrawer(dialog);
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull()); fireEvent.click(screen.getByRole('button', { name: /停\s*用/ }));
  await screen.findByText('停用后禁止新下注；已有投注的轮次仍会继续结算并发放奖励。'); expect(writes()).toHaveLength(0);
});
it('专属规则不可用时展示原因，不将缺失规则显示成零目标', async () => {
  settings.available = false; settings.unavailableReason = 'PVF 规则缺失'; settings.requiredClears = null; settings.totalWeeks = null;
  settings.weeklyRewards = []; settings.cumulativeRewards = [];
  open(); fireEvent.click(await screen.findByRole('button', { name: '设置活动' }));
  const dialog = await screen.findByRole('dialog'); expect(await within(dialog).findByText('PVF 规则缺失')).toBeTruthy();
  expect(within(dialog).queryByText('签到规则')).toBeNull(); expect(within(dialog).queryByText('每周奖励')).toBeNull();
});
it('专属规则读取失败可重试，重试成功展示最新规则并且没有写请求', async () => {
  const original = mocked.getMockImplementation()!; let failed = true;
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'events/weekly-attendance/settings' && failed) throw new ApiError(500, '规则读取超时');
    return original(path, payload);
  });
  open(); fireEvent.click(await screen.findByRole('button', { name: '设置活动' }));
  const dialog = await screen.findByRole('dialog'); await within(dialog).findByText('规则读取失败，请点击“刷新规则”重试。');
  failed = false; settings.requiredClears = 6; fireEvent.click(within(dialog).getByRole('button', { name: '刷新规则' }));
  await within(dialog).findByText('6 次推荐地下城'); expect(writes()).toHaveLength(0);
});
it('未配置的默认停用活动必须先填期限，不能直接启用或保存空期限', async () => {
  data.entries[0].startsAt = null; data.entries[0].endsAt = null;
  open(); expect((await screen.findByRole('button', { name: /启\s*用/ }) as HTMLButtonElement).disabled).toBe(true);
  await edit(); fireEvent.click(screen.getByRole('button', { name: '保存配置' }));
  await screen.findByText('请配置开始时间'); expect(writes()).toHaveLength(0);
});
it('从空期限输入原生datetime-local两端后捕获onChange并保存北京时间', async () => {
  data.entries[0].startsAt = null; data.entries[0].endsAt = null;
  open(); await edit();
  fireEvent.change(screen.getByLabelText('开始时间（北京时间 UTC+8）'), { target: { value: '2026-10-05T00:00:00' } });
  fireEvent.change(screen.getByLabelText('结束时间（北京时间 UTC+8）'), { target: { value: '2027-01-05T23:59:00' } });
  fireEvent.click(screen.getByRole('button', { name: '保存配置' }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('events', { eventId: 2208, expectedRevision: 8, action: 'configure', ...config,
    startsAt: '2026-10-04T16:00:00.000Z', endsAt: '2027-01-05T15:59:00.000Z' }));
});
it('保存全部展示字段和原版本；北京时间转ISO，保存不启用或新建期次', async () => {
  open(); await edit();
  expect((screen.getByLabelText('开始时间（北京时间 UTC+8）') as HTMLInputElement).value).toBe('2026-10-01T08:00');
  fireEvent.change(screen.getByLabelText('活动标题'), { target: { value: ' 新标题 ' } });
  fireEvent.click(screen.getByRole('button', { name: '保存配置' }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('events', { eventId: 2208, expectedRevision: 8, action: 'configure', ...config, title: '新标题' }));
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  expect((await screen.findAllByText('新标题')).length).toBeGreaterThan(0); expect(screen.getByText('season-one')).toBeTruthy(); expect(screen.getByText('已停用')).toBeTruthy();
});
it('结束早于开始时拒绝提交并使用统一通知', async () => {
  open(); await edit(); fireEvent.change(screen.getByLabelText('结束时间（北京时间 UTC+8）'), { target: { value: '2026-09-01T08:00' } });
  fireEvent.click(screen.getByRole('button', { name: '保存配置' }));
  await screen.findByText('请填写有效的起止时间，结束时间须晚于开始时间。'); expect(writes()).toHaveLength(0);
});
it('只修改展示信息保留服务端期限的秒和毫秒，避免误改已有进度的期限', async () => {
  data.entries[0].startsAt = '2026-10-01T00:00:17.123Z'; data.entries[0].endsAt = '2026-12-31T00:00:23.456Z';
  open(); await edit(); fireEvent.change(screen.getByLabelText('活动标题'), { target: { value: '说明更新' } });
  fireEvent.click(screen.getByRole('button', { name: '保存配置' }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('events', { eventId: 2208, expectedRevision: 8, action: 'configure', ...config,
    title: '说明更新', startsAt: '2026-10-01T00:00:17.123Z', endsAt: '2026-12-31T00:00:23.456Z' }));
});
it.each([false, true])('启停(%s)独立确认，只提交动作、活动ID与revision', async enabled => {
  data.entries[0].enabled = enabled; data.entries[0].status = enabled ? 'active' : 'disabled';
  open(); fireEvent.click(await screen.findByRole('button', { name: enabled ? /停\s*用/ : /启\s*用/ }));
  expect(await screen.findByText(/本期进度和已领取记录保留/)).toBeTruthy(); expect(writes()).toHaveLength(0);
  fireEvent.click(screen.getByRole('button', { name: enabled ? '确认停用' : '确认启用' }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('events', { eventId: 2208, expectedRevision: 8, action: enabled ? 'disable' : 'enable' }));
});
it('新建期次明确告知旧记录保留、新进度从头开始和默认停用，确认才写入', async () => {
  data.entries[0].enabled = true; data.entries[0].status = 'active';
  open(); fireEvent.click(await screen.findByRole('button', { name: /更\s*多/ })); fireEvent.click(await screen.findByText('新建期次'));
  const editor = await screen.findByRole('dialog'); fireEvent.click(within(editor).getByRole('button', { name: '新建期次' }));
  expect(await screen.findByText(/旧期次的进度、领取记录和已发邮件保留/)).toBeTruthy();
  expect(screen.getByText(/玩家进度从头开始，创建后默认停用/)).toBeTruthy(); expect(writes()).toHaveLength(0);
  fireEvent.click(screen.getByRole('button', { name: '确认新建期次' }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('events', { eventId: 2208, expectedRevision: 8, action: 'new-season', ...config }));
  expect(await screen.findByText('season-two')).toBeTruthy(); expect(screen.getByText('已停用')).toBeTruthy();
});
it('修改后取消须确认放弃，继续编辑保留字段且不会提交', async () => {
  open(); const editor = await edit(); fireEvent.change(screen.getByLabelText('活动标题'), { target: { value: '未保存标题' } });
  fireEvent.click(within(editor).getByRole('button', { name: /取\s*消/ }));
  await screen.findByText('放弃尚未保存的修改？', { selector: '.ant-modal-confirm-title' }); fireEvent.click(screen.getByRole('button', { name: '继续编辑' }));
  await waitFor(() => expect(screen.queryByText('放弃尚未保存的修改？', { selector: '.ant-modal-confirm-title' })).toBeNull());
  expect((screen.getByLabelText('活动标题') as HTMLInputElement).value).toBe('未保存标题'); expect(writes()).toHaveLength(0);
  fireEvent.click(within(editor).getByRole('button', { name: /取\s*消/ })); fireEvent.click(await screen.findByRole('button', { name: '放弃修改' }));
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
});
it('等待提交时重复点击只写入一次，不能取消正在保存的表单', async () => {
  const original = mocked.getMockImplementation()!; let resolve!: (value: EventList) => void;
  const pending = new Promise<EventList>(done => { resolve = done; });
  mocked.mockImplementation(async (path, payload) => path === 'events' && payload ? pending : original(path, payload));
  open(); const editor = await edit(); const save = screen.getByRole('button', { name: '保存配置' });
  fireEvent.click(save); fireEvent.click(save);
  await waitFor(() => expect(writes()).toHaveLength(1)); expect((within(editor).getByRole('button', { name: /取\s*消/ }) as HTMLButtonElement).disabled).toBe(true);
  await act(async () => { resolve(data); }); await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
});
it.each([409, 500, 'network', 'unknown'])('写入失败 %s 后锁写，重读成功后使用新版本恢复操作', async failure => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'events' && payload) throw failure === 'network' ? new TypeError('offline') : new ApiError(typeof failure === 'number' ? failure : 400, '活动结果未确认', failure === 'unknown' ? 'unknown' : undefined);
    return original(path, payload);
  });
  open(); const editor = await edit(); fireEvent.click(screen.getByRole('button', { name: '保存配置' }));
  await screen.findByText('请先重新读取生效活动'); expect(screen.queryByRole('button', { name: '保存配置' })).toBeNull();
  expect((screen.getByRole('button', { name: '通用配置' }) as HTMLButtonElement).disabled).toBe(true);
  data.entries[0].revision = 10;
  fireEvent.click(within(editor).getByRole('button', { name: '重新读取生效活动' }));
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  await waitFor(() => expect((screen.getByRole('button', { name: '通用配置' }) as HTMLButtonElement).disabled).toBe(false));
  expect(screen.getByText(/v10/)).toBeTruthy(); expect(writes()).toHaveLength(1);
});
it('重读失败保持写锁，随后重读成功才恢复', async () => {
  const original = mocked.getMockImplementation()!; let failRead = false;
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'events' && payload) { failRead = true; throw new ApiError(500, '保存超时'); }
    if (path === 'events' && failRead) throw new ApiError(500, '读取失败');
    return original(path, payload);
  });
  open(); const editor = await edit(); fireEvent.click(screen.getByRole('button', { name: '保存配置' })); await screen.findByText('请先重新读取生效活动');
  fireEvent.click(within(editor).getByRole('button', { name: '重新读取生效活动' })); await screen.findByText('读取失败');
  expect(screen.queryByRole('button', { name: '通用配置' })).toBeNull(); expect(screen.getByText('请先重新读取生效活动')).toBeTruthy();
  failRead = false; fireEvent.click(screen.getAllByRole('button', { name: /重新读取生效活动/ }).at(-1)!);
  await waitFor(() => expect((screen.getByRole('button', { name: '通用配置' }) as HTMLButtonElement).disabled).toBe(false));
  expect(writes()).toHaveLength(1);
});
it('普通字段拒绝保持可编辑，不要求重新读取', async () => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => { if (path === 'events' && payload) throw new ApiError(400, '已有进度，不能修改期限'); return original(path, payload); });
  open(); await edit(); fireEvent.click(screen.getByRole('button', { name: '保存配置' })); await screen.findByText('已有进度，不能修改期限');
  expect(screen.getByRole('button', { name: '保存配置' })).toBeTruthy(); expect((screen.getByLabelText('活动标题') as HTMLInputElement).disabled).toBe(false);
});
it('规则不可用仍显示原因和奖励，但不可启用', async () => {
  data.entries[0].available = false; data.entries[0].status = 'unavailable'; data.entries[0].unavailableReason = '奖励物品缺失';
  open(); expect(await screen.findByText('奖励物品缺失')).toBeTruthy(); expect(screen.getByText('规则不可用')).toBeTruthy();
  expect((screen.getByRole('button', { name: /启\s*用/ }) as HTMLButtonElement).disabled).toBe(true); expect(screen.getByRole('button', { name: '通用配置' })).toBeTruthy();
});
it.each([401, 403])('写入权限失败 %i 退出编辑并由AdminApp处理权限', async status => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => { if (path === 'events' && payload) throw new ApiError(status, '权限已失效'); return original(path, payload); });
  open(); await edit(); fireEvent.click(screen.getByRole('button', { name: '保存配置' }));
  if (status === 401) await screen.findByLabelText('管理员账号'); else { await screen.findByText('没有访问此页面的权限'); expect(rail().queryByText('活动管理')).toBeNull(); }
  expect(screen.queryByRole('dialog')).toBeNull();
});
it('缺少All权限隐藏入口，直接访问不读取活动', async () => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => path === 'auth/session' ? { ...identity, permissions: ['GrantCera'] } : original(path, payload));
  open(); await screen.findByText('没有访问此页面的权限'); expect(rail().queryByText('活动管理')).toBeNull();
  expect(mocked.mock.calls.some(([path]) => path === 'events')).toBe(false);
});
