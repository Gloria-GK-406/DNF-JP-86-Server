import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App, ConfigProvider, Modal } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { AdminApp } from './AdminApp';
import { api, ApiError } from './api';
import type { EventEntry, EventList } from './EventsView';

vi.mock('./api', async original => ({ ...await original<typeof import('./api')>(), api: vi.fn() }));
const mocked = vi.mocked(api);
const identity = { id: 1, loginName: '活动管理员', permissions: ['QueryPlayers', 'SendMail', 'GrantCera', 'ViewAudit'], expiresAt: '2030-01-01T00:00:00Z' };
let data: EventList;
const writes = () => mocked.mock.calls.filter(([path, payload]) => path === 'events' && payload);
function open() { return render(<ConfigProvider theme={{ token: { motion: false } }}><App><MemoryRouter initialEntries={['/events']}><AdminApp /></MemoryRouter></App></ConfigProvider>); }
async function edit() { fireEvent.click(await screen.findByRole('button', { name: '编辑配置' })); return screen.findByRole('dialog'); }
const config = { title: '累计周签到', shortName: '周签到', description: '完成推荐地下城后签到', startNotice: '签到开启', endNotice: '签到结束',
  startsAt: '2026-10-01T00:00:00.000Z', endsAt: '2026-12-31T00:00:00.000Z' };
afterEach(() => Modal.destroyAll());
beforeEach(() => {
  data = { entries: [{ eventId: 2208, type: 'weekly-attendance', revision: 8, seasonId: 'season-one', enabled: false, status: 'disabled', ...config,
    sourcePath: 'event/chn_event/chn_totalattendance.evt', available: true, unavailableReason: null, requiredClears: 22, totalWeeks: 12,
    weeklyRewards: [{ attendanceNo: 1, itemId: 90001, count: 2 }], cumulativeRewards: [{ attendanceNo: 4, itemId: 90004, count: 1 }], updatedAt: null, updatedBy: null }] };
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'auth/status') return { initialized: true }; if (path === 'auth/session') return identity;
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
    throw new Error('Unexpected request: ' + path);
  });
});

it('All导航展示注册活动、期次状态、规则来源和两类奖励，规则不可编辑', async () => {
  open(); expect(await screen.findByRole('menuitem', { name: '活动管理' })).toBeTruthy();
  expect(await screen.findByText('season-one')).toBeTruthy(); expect(screen.getByText('已停用')).toBeTruthy();
  expect(screen.getByText(/event\/chn_event\/chn_totalattendance.evt/)).toBeTruthy();
  expect(screen.getByText(/物品 90001 × 2/)).toBeTruthy(); expect(screen.getByText(/物品 90004 × 1/)).toBeTruthy();
  expect(screen.queryByRole('dialog')).toBeNull(); await edit();
  expect(screen.queryByLabelText(/奖励/)).toBeNull(); expect(screen.queryByLabelText(/签到目标/)).toBeNull();
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
  expect(await screen.findByText('新标题')).toBeTruthy(); expect(screen.getByText('season-one')).toBeTruthy(); expect(screen.getByText('已停用')).toBeTruthy();
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
  open(); fireEvent.click(await screen.findByRole('button', { name: '新建期次' }));
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
  await screen.findByText('请重新读取生效活动后再操作'); expect(screen.queryByRole('button', { name: '保存配置' })).toBeNull();
  expect((screen.getByRole('button', { name: '编辑配置' }) as HTMLButtonElement).disabled).toBe(true);
  data.entries[0].revision = 10;
  fireEvent.click(within(editor).getByRole('button', { name: '重新读取生效活动' }));
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  await waitFor(() => expect((screen.getByRole('button', { name: '编辑配置' }) as HTMLButtonElement).disabled).toBe(false));
  expect(screen.getByText('配置版本 10')).toBeTruthy(); expect(writes()).toHaveLength(1);
});
it('重读失败保持写锁，随后重读成功才恢复', async () => {
  const original = mocked.getMockImplementation()!; let failRead = false;
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'events' && payload) { failRead = true; throw new ApiError(500, '保存超时'); }
    if (path === 'events' && failRead) throw new ApiError(500, '读取失败');
    return original(path, payload);
  });
  open(); const editor = await edit(); fireEvent.click(screen.getByRole('button', { name: '保存配置' })); await screen.findByText('请重新读取生效活动后再操作');
  fireEvent.click(within(editor).getByRole('button', { name: '重新读取生效活动' })); await screen.findByText('读取失败');
  expect(screen.queryByRole('button', { name: '编辑配置' })).toBeNull(); expect(screen.getByText('请重新读取生效活动后再操作')).toBeTruthy();
  failRead = false; fireEvent.click(screen.getByRole('button', { name: '重新读取生效活动' }));
  await waitFor(() => expect((screen.getByRole('button', { name: '编辑配置' }) as HTMLButtonElement).disabled).toBe(false));
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
  expect((screen.getByRole('button', { name: /启\s*用/ }) as HTMLButtonElement).disabled).toBe(true); expect(screen.getByRole('button', { name: '编辑配置' })).toBeTruthy();
});
it.each([401, 403])('写入权限失败 %i 退出编辑并由AdminApp处理权限', async status => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => { if (path === 'events' && payload) throw new ApiError(status, '权限已失效'); return original(path, payload); });
  open(); await edit(); fireEvent.click(screen.getByRole('button', { name: '保存配置' }));
  if (status === 401) await screen.findByLabelText('管理员账号'); else { await screen.findByText('当前页面不可访问'); expect(screen.queryByRole('menuitem', { name: '活动管理' })).toBeNull(); }
  expect(screen.queryByRole('dialog')).toBeNull();
});
it('缺少All权限隐藏入口，直接访问不读取活动', async () => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => path === 'auth/session' ? { ...identity, permissions: ['GrantCera'] } : original(path, payload));
  open(); await screen.findByText('当前页面不可访问'); expect(screen.queryByRole('menuitem', { name: '活动管理' })).toBeNull();
  expect(mocked.mock.calls.some(([path]) => path === 'events')).toBe(false);
});
