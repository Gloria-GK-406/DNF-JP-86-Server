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
function open() { render(<ConfigProvider theme={{ token: { motion: false } }}><App><JoustSettings title="骑士马战大竞猜"
  onClose={closed} onExpired={expired} onDenied={denied} /></App></ConfigProvider>); }
async function ready() { open(); await screen.findByText('event/chn_event/chn_joust.evt'); }
function input(label: string) { return screen.getByLabelText(label) as HTMLInputElement; }
function change(label: string, value: string) { fireEvent.change(input(label), { target: { value } }); }
afterEach(() => Modal.destroyAll());
beforeEach(() => {
  expired.mockClear(); denied.mockClear(); closed.mockClear();
  settings = { eventId: 2365, revision: 4, sourcePath: 'event/chn_event/chn_joust.evt', available: true, unavailableReason: null,
    schedule: { dailyStartTime: '10:00', roundsPerDay: 7, roundIntervalMinutes: 120, bettingMinutes: 90, lockMinutes: 10, stageSeconds: 200 },
    parameters: { strongMultiplier: 1.15, normalMultiplier: 1, weakMultiplier: .85, counterMultiplier: 1.25, historyPrior: 10, forecastWeight: .4, bettingScale: 1000 },
    minimumLevel: 17, maximumBetting: 1000, knights: [{ index: 0, name: '白骑士', attackType: 1, win: [1, 2], loss: [3, 4] }],
    currencies: [{ itemId: 80001, itemName: '马战竞猜币', archivedExpiresAt: null }, { itemId: 80002, itemName: null, archivedExpiresAt: null }],
    reward: { itemId: 80003, itemName: '<b>冠军纪念币</b>', archivedExpiresAt: null }, bettingReward: null, updatedAt: null, updatedBy: null };
  mocked.mockImplementation(async (request, payload) => {
    if (request !== path) throw new Error('Unexpected request: ' + request);
    if (payload) { const change = payload as { expectedRevision: number; parameters: JoustSettingsData['parameters'] };
      settings = { ...settings, revision: settings.revision + 1, parameters: change.parameters }; }
    return structuredClone(settings);
  });
});
it('显示名称和ID，时间页只读，打开设置不查询当前场次或物品目录', async () => {
  await ready(); const dialog = screen.getByRole('dialog');
  expect(within(dialog).getByText('白骑士（0）')).toBeTruthy(); expect(within(dialog).getByText(/<b>冠军纪念币<\/b>/)).toBeTruthy();
  expect(within(dialog).getByText(/名称未找到/)).toBeTruthy(); expect(within(dialog).getAllByRole('spinbutton')).toHaveLength(7);
  fireEvent.click(screen.getByRole('tab', { name: '每日赛程' }));
  expect(await screen.findByText('10:00（北京时间）')).toBeTruthy();
  const panel = screen.getByRole('tabpanel'); expect(within(panel).queryAllByRole('spinbutton')).toHaveLength(0);
  expect(within(panel).getByText('第 7 场')).toBeTruthy(); expect(within(panel).queryByText('第 8 场')).toBeNull(); expect(screen.queryByRole('button', { name: '保存玩法参数' })).toBeNull();
  expect(mocked.mock.calls.every(([request]) => request === path)).toBe(true); expect(writes()).toHaveLength(0);
});
it('仅保存玩法参数和revision，服务器返回版本用于下一次保存', async () => {
  await ready(); change('强势战斗力倍率', '1.5'); fireEvent.click(screen.getByRole('button', { name: '保存玩法参数' }));
  await screen.findByText('玩法参数已保存，将从新场次生效。');
  expect(writes()[0]).toEqual([path, { expectedRevision: 4, parameters: { ...settings.parameters, strongMultiplier: 1.5 } }]);
  change('投注衰减基数', '2000'); fireEvent.click(screen.getByRole('button', { name: '保存玩法参数' }));
  await waitFor(() => expect(writes()).toHaveLength(2)); expect((writes()[1][1] as { expectedRevision: number }).expectedRevision).toBe(5);
});
it.each([['弱势战斗力倍率', '2'], ['攻击克制倍率', '.5'], ['历史战绩先验场数', '1.5'], ['投注衰减基数', ''], ['本期预测修正权重', '2']])('拒绝无效参数%s=%s', async (label, value) => {
  await ready(); change(label, value); fireEvent.click(screen.getByRole('button', { name: '保存玩法参数' }));
  await screen.findByText(/请填写有效参数/); expect(writes()).toHaveLength(0);
});
it('初始读取中和读取失败不能保存，重新读取后解锁', async () => {
  let reject!: (error: unknown) => void; mocked.mockImplementation(() => new Promise((_, fail) => { reject = fail; })); open();
  expect((screen.getByRole('button', { name: '保存玩法参数' }) as HTMLButtonElement).disabled).toBe(true);
  await act(async () => reject(new ApiError(500, '初始读取失败'))); await screen.findByText('设置读取失败，请重新读取。编辑内容已保留。');
  mocked.mockResolvedValue(structuredClone(settings)); fireEvent.click(screen.getByRole('button', { name: '重新读取生效参数' }));
  await screen.findByText('event/chn_event/chn_joust.evt'); expect(input('强势战斗力倍率').disabled).toBe(false);
});
it('PVF不可用不能保存玩法参数', async () => {
  settings.available = false; settings.unavailableReason = '马战PVF规则缺失'; await ready();
  expect(screen.getByText('马战PVF规则缺失')).toBeTruthy(); expect(input('强势战斗力倍率').disabled).toBe(true);
  expect((screen.getByRole('button', { name: '保存玩法参数' }) as HTMLButtonElement).disabled).toBe(true);
});
it.each([409, 500, 'network', 'unknown'])('失败%s保留草稿，重读核对后才按新revision保存', async failure => {
  const original = mocked.getMockImplementation()!; let fail = true;
  mocked.mockImplementation((request, payload) => payload && fail ? Promise.reject(failure === 'network' ? new TypeError('offline') :
    new ApiError(typeof failure === 'number' ? failure : 400, '保存待核对', failure === 'unknown' ? 'unknown' : undefined)) : original(request, payload));
  await ready(); change('强势战斗力倍率', '1.8'); fireEvent.click(screen.getByRole('button', { name: '保存玩法参数' }));
  await screen.findByText('请重新读取生效参数后再保存'); expect(input('强势战斗力倍率').value).toBe('1.8');
  expect((screen.getByRole('button', { name: '核对后保存参数' }) as HTMLButtonElement).disabled).toBe(true);
  settings.revision = 12; settings.parameters.strongMultiplier = 1.6;
  fireEvent.click(screen.getByRole('button', { name: '重新读取生效参数' })); await screen.findByText('当前生效参数');
  await waitFor(() => expect((screen.getByRole('button', { name: '核对后保存参数' }) as HTMLButtonElement).disabled).toBe(false));
  expect(input('强势战斗力倍率').value).toBe('1.8'); fail = false; fireEvent.click(screen.getByRole('button', { name: '核对后保存参数' }));
  await screen.findByText('确认保存核对后的玩法参数？', { selector: '.ant-modal-confirm-title' }); expect(writes()).toHaveLength(1);
  fireEvent.click(screen.getByRole('button', { name: '确认保存参数' })); await waitFor(() => expect(writes()).toHaveLength(2));
  expect((writes()[1][1] as { expectedRevision: number }).expectedRevision).toBe(12);
});
it('保存中只写一次，并禁止切换页签、关闭或编辑', async () => {
  const original = mocked.getMockImplementation()!; let resolve!: (value: JoustSettingsData) => void;
  mocked.mockImplementation((request, payload) => payload ? new Promise(done => { resolve = done; }) : original(request, payload));
  await ready(); const save = screen.getByRole('button', { name: '保存玩法参数' }); fireEvent.click(save); fireEvent.click(save);
  await waitFor(() => expect(writes()).toHaveLength(1)); expect(input('强势战斗力倍率').disabled).toBe(true);
  fireEvent.click(screen.getByRole('tab', { name: '当前场次' })); expect(mocked.mock.calls.every(([request]) => request === path)).toBe(true);
  expect((screen.getByRole('button', { name: /关\s*闭/ }) as HTMLButtonElement).disabled).toBe(true);
  await act(async () => resolve({ ...settings, revision: 5 }));
});
it('放弃草稿要确认，取消继续编辑，无写入', async () => {
  await ready(); change('强势战斗力倍率', '1.8'); fireEvent.click(screen.getByRole('button', { name: /关\s*闭/ }));
  await screen.findByText('放弃尚未保存的玩法参数？', { selector: '.ant-modal-confirm-title' }); fireEvent.click(screen.getByRole('button', { name: '继续编辑' }));
  expect(input('强势战斗力倍率').value).toBe('1.8'); expect(closed).not.toHaveBeenCalled(); expect(writes()).toHaveLength(0);
});
it.each([401, 403])('权限失败%i沿用Admin权限处理', async status => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation((request, payload) => payload ? Promise.reject(new ApiError(status, '权限失效')) : original(request, payload));
  await ready(); fireEvent.click(screen.getByRole('button', { name: '保存玩法参数' }));
  await waitFor(() => expect(status === 401 ? expired : denied).toHaveBeenCalledTimes(1));
});
