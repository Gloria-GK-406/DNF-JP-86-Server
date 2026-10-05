import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App, ConfigProvider, Modal } from 'antd';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { api, ApiError } from '../api';
import { JoustSettings, type JoustSettingsData } from './JoustSettings';

vi.mock('../api', async original => ({ ...await original<typeof import('../api')>(), api: vi.fn() }));
const mocked = vi.mocked(api); const path = 'events/joust/settings';
let settings: JoustSettingsData;
const expired = vi.fn(); const denied = vi.fn(); const closed = vi.fn();
const writes = () => mocked.mock.calls.filter(([, payload]) => payload !== undefined);
function open() { return render(<ConfigProvider theme={{ token: { motion: false } }}><App><JoustSettings title="骑士马战大竞猜"
  onClose={closed} onExpired={expired} onDenied={denied} /></App></ConfigProvider>); }
async function ready() { open(); await screen.findByText('event/chn_event/chn_joust.evt'); }
function input(label: string) { return screen.getByLabelText(label) as HTMLInputElement; }
function change(label: string, value: string) { fireEvent.change(input(label), { target: { value } }); }
function edited() {
  change('每天开始时间（北京时间 UTC+8）', '08:30'); change('每天期数', '5'); change('每期间隔（分钟）', '150');
  change('下注时长（分钟）', '75'); change('封盘时长（分钟）', '15'); change('每个比赛阶段（秒）', '300');
}
function expectDraft() {
  expect(input('每天开始时间（北京时间 UTC+8）').value).toBe('08:30'); expect(input('每天期数').value).toBe('5');
  expect(input('每期间隔（分钟）').value).toBe('150'); expect(input('下注时长（分钟）').value).toBe('75');
  expect(input('封盘时长（分钟）').value).toBe('15'); expect(input('每个比赛阶段（秒）').value).toBe('300');
}
afterEach(() => Modal.destroyAll());
beforeEach(() => {
  expired.mockClear(); denied.mockClear(); closed.mockClear();
  settings = { eventId: 2365, revision: 4, sourcePath: 'event/chn_event/chn_joust.evt', available: true, unavailableReason: null,
    schedule: { dailyStartTime: '10:00', roundsPerDay: 7, roundIntervalMinutes: 120, bettingMinutes: 90, lockMinutes: 10, stageSeconds: 200 },
    minimumLevel: 17, maximumBetting: 1000, knights: [{ index: 0, name: '白骑士', attackType: 1, win: [1, 2], loss: [3, 4] }],
    currencies: [{ itemId: 80001, itemName: '马战竞猜币', archivedExpiresAt: '2016-01-01T00:00:00Z' },
      { itemId: 80002, itemName: null, archivedExpiresAt: null }],
    reward: { itemId: 80003, itemName: '冠军纪念币', archivedExpiresAt: null },
    bettingReward: { itemId: 80004, itemName: '参与凭证', archivedExpiresAt: null }, updatedAt: null, updatedBy: null };
  mocked.mockImplementation(async (request, payload) => {
    if (request !== path) throw new Error('Unexpected request: ' + request);
    if (payload) { const change = payload as { expectedRevision: number; schedule: JoustSettingsData['schedule'] };
      settings = { ...settings, revision: settings.revision + 1, schedule: change.schedule }; }
    return structuredClone(settings);
  });
});

it('显示六项赛程、PVF名称和ID，缺失名称保留ID且没有额外目录查询', async () => {
  settings.reward!.itemName = '<b>冠军纪念币</b>'; await ready();
  const dialog = screen.getByRole('dialog');
  expect(input('每天开始时间（北京时间 UTC+8）').value).toBe('10:00'); expect(input('每天期数').value).toBe('7');
  expect(within(dialog).getByText('白骑士（0）')).toBeTruthy(); expect(within(dialog).getByText(/马战竞猜币/)).toBeTruthy();
  for (const id of ['80001', '80002', '80003', '80004']) expect(within(dialog).getByText(id)).toBeTruthy();
  expect(within(dialog).getByText(/名称未找到/)).toBeTruthy(); expect(within(dialog).getByText(/<b>冠军纪念币<\/b>/)).toBeTruthy();
  expect(within(dialog).getAllByRole('spinbutton')).toHaveLength(5);
  expect(mocked.mock.calls.every(([request]) => request === path)).toBe(true); expect(writes()).toHaveLength(0);
});
it('只保存六项赛程和独立revision，采用服务器返回版本继续保存', async () => {
  await ready(); edited(); fireEvent.click(screen.getByRole('button', { name: '保存赛程' }));
  await screen.findByText('马战赛程已保存，已生成轮次沿用原赛程。');
  expect(writes()[0]).toEqual([path, { expectedRevision: 4, schedule: { dailyStartTime: '08:30', roundsPerDay: 5,
    roundIntervalMinutes: 150, bettingMinutes: 75, lockMinutes: 15, stageSeconds: 300 } }]);
  change('每天期数', '4'); fireEvent.click(screen.getByRole('button', { name: '保存赛程' }));
  await waitFor(() => expect(writes()).toHaveLength(2)); expect((writes()[1][1] as { expectedRevision: number }).expectedRevision).toBe(5);
});
it.each(['cross-midnight', 'stage-overflow', 'zero-result-window', 'fraction', 'empty'])('无效赛程%s不写入并使用统一通知', async invalid => {
  await ready();
  if (invalid === 'cross-midnight') change('每天开始时间（北京时间 UTC+8）', '11:00');
  if (invalid === 'stage-overflow') change('每个比赛阶段（秒）', '1000');
  if (invalid === 'zero-result-window') change('每个比赛阶段（秒）', '400');
  if (invalid === 'fraction') change('每天期数', '1.5');
  if (invalid === 'empty') change('每天期数', '');
  fireEvent.click(screen.getByRole('button', { name: '保存赛程' })); await screen.findByText(/请填写有效赛程/); expect(writes()).toHaveLength(0);
});
it('初始读取中禁止保存，读取失败重试后才可编辑保存', async () => {
  let reject!: (error: unknown) => void; mocked.mockImplementation(() => new Promise((_, fail) => { reject = fail; }));
  open(); expect((screen.getByRole('button', { name: '保存赛程' }) as HTMLButtonElement).disabled).toBe(true);
  await act(async () => reject(new ApiError(500, '初始读取失败')));
  await screen.findByText('赛程读取失败，请重新读取成功后再保存。编辑内容已保留。');
  expect((screen.getByRole('button', { name: '保存赛程' }) as HTMLButtonElement).disabled).toBe(true);
  mocked.mockResolvedValue(structuredClone(settings)); fireEvent.click(screen.getByRole('button', { name: '重新读取生效赛程' }));
  await screen.findByText('event/chn_event/chn_joust.evt'); expect(input('每天期数').disabled).toBe(false); expect(writes()).toHaveLength(0);
});
it('规则不可用保留赛程和原因，字段及保存只读', async () => {
  settings.available = false; settings.unavailableReason = '马战PVF规则缺失'; settings.minimumLevel = null;
  await ready(); expect(screen.getByText('马战PVF规则缺失')).toBeTruthy();
  expect(input('每天期数').value).toBe('7'); expect(input('每天期数').disabled).toBe(true);
  expect((screen.getByRole('button', { name: '保存赛程' }) as HTMLButtonElement).disabled).toBe(true); expect(writes()).toHaveLength(0);
});
it('保存中重复点击只写一次，无法关闭或编辑', async () => {
  const original = mocked.getMockImplementation()!; let resolve!: (value: JoustSettingsData) => void;
  mocked.mockImplementation((request, payload) => payload ? new Promise(done => { resolve = done; }) : original(request, payload));
  await ready(); const save = screen.getByRole('button', { name: '保存赛程' }); fireEvent.click(save); fireEvent.click(save);
  await waitFor(() => expect(writes()).toHaveLength(1)); expect(input('每天期数').disabled).toBe(true);
  expect((screen.getByRole('button', { name: /关\s*闭/ }) as HTMLButtonElement).disabled).toBe(true);
  await act(async () => resolve({ ...settings, revision: 5 })); await screen.findByText('马战赛程已保存，已生成轮次沿用原赛程。');
});
it.each([409, 500, 'network', 'unknown'])('失败%s保留全部草稿，重读后核对并使用新版本保存', async failure => {
  const original = mocked.getMockImplementation()!; let fail = true;
  mocked.mockImplementation((request, payload) => {
    if (payload && fail) return Promise.reject(failure === 'network' ? new TypeError('offline') :
      new ApiError(typeof failure === 'number' ? failure : 400, '保存结果待核对', failure === 'unknown' ? 'unknown' : undefined));
    return original(request, payload);
  });
  await ready(); edited(); fireEvent.click(screen.getByRole('button', { name: '保存赛程' }));
  await screen.findByText('请重新读取生效赛程后再保存'); expectDraft();
  expect((screen.getByRole('button', { name: '核对后保存赛程' }) as HTMLButtonElement).disabled).toBe(true);
  settings.revision = 12; settings.schedule.dailyStartTime = '09:00';
  fireEvent.click(screen.getByRole('button', { name: '重新读取生效赛程' })); await screen.findByText(/09:00 开始/);
  expectDraft(); expect(writes()).toHaveLength(1);
  fail = false; fireEvent.click(screen.getByRole('button', { name: '核对后保存赛程' }));
  await screen.findByText('确认保存核对后的赛程？', { selector: '.ant-modal-confirm-title' });
  expect(screen.getByText(/服务器配置版本 12/)).toBeTruthy(); expect(writes()).toHaveLength(1);
  fireEvent.click(screen.getByRole('button', { name: '确认保存赛程' }));
  await waitFor(() => expect(writes()).toHaveLength(2)); expect((writes()[1][1] as { expectedRevision: number }).expectedRevision).toBe(12); expectDraft();
});
it('恢复重读失败仍锁写且保留草稿，后续成功才解锁', async () => {
  const original = mocked.getMockImplementation()!; let failRead = false;
  mocked.mockImplementation((request, payload) => {
    if (payload) { failRead = true; return Promise.reject(new ApiError(500, '保存超时')); }
    if (failRead) return Promise.reject(new ApiError(500, '重读失败'));
    return original(request, payload);
  });
  await ready(); edited(); fireEvent.click(screen.getByRole('button', { name: '保存赛程' }));
  await screen.findByText('请重新读取生效赛程后再保存'); fireEvent.click(screen.getByRole('button', { name: '重新读取生效赛程' }));
  await screen.findByText('赛程读取失败，请重新读取成功后再保存。编辑内容已保留。'); expectDraft();
  fireEvent.click(screen.getByRole('button', { name: '核对后保存赛程' })); expect(writes()).toHaveLength(1);
  failRead = false; settings.revision = 15; fireEvent.click(screen.getByRole('button', { name: '重新读取生效赛程' }));
  await waitFor(() => expect((screen.getByRole('button', { name: '核对后保存赛程' }) as HTMLButtonElement).disabled).toBe(false)); expectDraft();
});
it('不确定保存后的重读保留提交意图，即使原草稿与旧生效值相同', async () => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation((request, payload) => payload ? Promise.reject(new ApiError(409, '赛程已变更')) : original(request, payload));
  await ready(); fireEvent.click(screen.getByRole('button', { name: '保存赛程' })); await screen.findByText('请重新读取生效赛程后再保存');
  settings.schedule.dailyStartTime = '09:00'; settings.revision = 9;
  fireEvent.click(screen.getByRole('button', { name: '重新读取生效赛程' })); await screen.findByText(/09:00 开始/);
  expect(input('每天开始时间（北京时间 UTC+8）').value).toBe('10:00'); expect(writes()).toHaveLength(1);
});
it('普通400保持全部草稿可继续编辑，刷新规则也保留草稿', async () => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation((request, payload) => payload ? Promise.reject(new ApiError(400, '赛程拒绝')) : original(request, payload));
  await ready(); edited(); fireEvent.click(screen.getByRole('button', { name: '保存赛程' })); await screen.findByText('赛程拒绝');
  expectDraft(); expect(input('每天期数').disabled).toBe(false); expect(screen.queryByText('请重新读取生效赛程后再保存')).toBeNull();
  fireEvent.click(screen.getByRole('button', { name: '刷新规则' }));
  await waitFor(() => expect((screen.getByRole('button', { name: '保存赛程' }) as HTMLButtonElement).disabled).toBe(false)); expectDraft();
});
it('未编辑的正常刷新同步服务器新赛程与revision', async () => {
  await ready(); settings.schedule.dailyStartTime = '09:00'; settings.revision = 9;
  fireEvent.click(screen.getByRole('button', { name: '刷新规则' }));
  await waitFor(() => expect(input('每天开始时间（北京时间 UTC+8）').value).toBe('09:00'));
  expect(screen.queryByText('当前生效赛程')).toBeNull(); fireEvent.click(screen.getByRole('button', { name: '保存赛程' }));
  await waitFor(() => expect(writes()).toHaveLength(1));
  expect((writes()[0][1] as { expectedRevision: number }).expectedRevision).toBe(9);
  expect((writes()[0][1] as { schedule: JoustSettingsData['schedule'] }).schedule.dailyStartTime).toBe('09:00');
});
it('保留已编辑草稿的正常刷新遇到外部修改时要求核对，避免无提示覆盖新赛程', async () => {
  await ready(); edited(); settings.schedule.dailyStartTime = '09:00'; settings.revision = 9;
  fireEvent.click(screen.getByRole('button', { name: '刷新规则' })); await screen.findByText(/09:00 开始/); expectDraft();
  expect(writes()).toHaveLength(0); fireEvent.click(screen.getByRole('button', { name: '核对后保存赛程' }));
  await screen.findByText('确认保存核对后的赛程？', { selector: '.ant-modal-confirm-title' }); expect(writes()).toHaveLength(0);
  fireEvent.click(screen.getByRole('button', { name: '确认保存赛程' }));
  await waitFor(() => expect(writes()).toHaveLength(1)); expect((writes()[0][1] as { expectedRevision: number }).expectedRevision).toBe(9); expectDraft();
});
it('关闭有修改时需确认，继续编辑保留草稿且没有写请求', async () => {
  await ready(); edited(); fireEvent.click(screen.getByRole('button', { name: /关\s*闭/ }));
  await screen.findByText('放弃尚未保存的赛程修改？', { selector: '.ant-modal-confirm-title' });
  fireEvent.click(screen.getByRole('button', { name: '继续编辑' }));
  await waitFor(() => expect(screen.queryByText('放弃尚未保存的赛程修改？', { selector: '.ant-modal-confirm-title' })).toBeNull());
  expectDraft(); expect(closed).not.toHaveBeenCalled(); expect(writes()).toHaveLength(0);
});
it.each([401, 403])('保存权限失败%i沿用Admin权限处理', async status => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation((request, payload) => payload ? Promise.reject(new ApiError(status, '权限失效')) : original(request, payload));
  await ready(); fireEvent.click(screen.getByRole('button', { name: '保存赛程' }));
  await waitFor(() => expect(status === 401 ? expired : denied).toHaveBeenCalledTimes(1));
});
