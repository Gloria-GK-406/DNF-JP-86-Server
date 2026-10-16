import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import { beforeEach, expect, it, vi } from 'vitest';
import { api, ApiError } from '../api';
import { JoustCurrentRound, type JoustCurrentRoundData } from './JoustCurrentRound';
vi.mock('../api', async original => ({ ...await original<typeof import('../api')>(), api: vi.fn() }));
const mocked = vi.mocked(api); const path = 'events/joust/current-round';
const writing = vi.fn(), expired = vi.fn(), denied = vi.fn();
let data: JoustCurrentRoundData;
const writes = () => mocked.mock.calls.filter(([, payload]) => payload !== undefined);
function capturePoll(onPoll: (callback: () => void) => void) {
  const original = window.setInterval;
  return vi.spyOn(window, 'setInterval').mockImplementation((callback, delay, ...args) => {
    if (delay === 5000) { onPoll(callback as () => void); return 123; }
    return original(callback, delay, ...args);
  });
}
async function open() {
  render(<ConfigProvider theme={{ token: { motion: false } }}><App><JoustCurrentRound onExpired={expired} onDenied={denied} onWriting={writing} /></App></ConfigProvider>);
  await screen.findByText('白骑士0（0）');
  await waitFor(() => expect((screen.getAllByRole('radio')[0] as HTMLInputElement).disabled).toBe(false));
}
beforeEach(() => {
  writing.mockClear(); expired.mockClear(); denied.mockClear();
  data = { roundId: '1791252000', roundNo: 13, revision: 3, active: true, phase: 'Locked', serverTime: '2026-10-06T03:35:00Z',
    startsAt: '2026-10-06T02:00:00Z', endsAt: '2026-10-06T04:00:00Z', nextTransitionAt: '2026-10-06T03:40:00Z',
    championDeadline: '2026-10-06T03:40:00Z', canSetChampion: true, forcedChampionIndex: null, updatedAt: null, updatedBy: null,
    knights: Array.from({ length: 8 }, (_, index) => ({ index, name: `白骑士${index}`, attackType: 1, condition: 1, combatMultiplier: 1,
      championshipProbability: .125, odds: 2, bettingAmount: 100 + index, statistics: { wins: 3, losses: 1, championships: 1, winRate: 75 } })) };
  mocked.mockImplementation(async (request, payload) => {
    if (request === `${path}/champion` && payload) { const change = payload as { knightIndex: number | null };
      data = { ...data, revision: data.revision + 1, forcedChampionIndex: change.knightIndex, updatedBy: 'admin', updatedAt: data.serverTime }; }
    else if (request !== path) throw new Error(request);
    return structuredClone(data);
  });
});
it.each(['Betting', 'Locked'])('阶段%s可选择和取消；单选不写入，显式提交只含本场id和revision', async phase => {
  data.phase = phase;
  await open(); expect(screen.getAllByRole('radio')).toHaveLength(8); expect(screen.getAllByText('75.0%')).toHaveLength(8);
  expect(screen.getAllByText('12.5%')).toHaveLength(8); expect(screen.getAllByText('2.0×')).toHaveLength(8);
  fireEvent.click(screen.getAllByRole('radio')[2]); expect(writes()).toHaveLength(0);
  fireEvent.click(screen.getByRole('button', { name: '设为本场冠军' })); fireEvent.click(await screen.findByRole('button', { name: '就它了' })); await screen.findByText('本场内定冠军已保存。');
  expect(writes()).toEqual([[`${path}/champion`, { roundId: '1791252000', expectedRevision: 3, knightIndex: 2 }]]);
  expect(writing.mock.calls).toEqual([[true], [false]]);
  fireEvent.click(screen.getByRole('button', { name: '取消内定' })); await screen.findByText('已取消本场内定冠军，恢复原抽签结果。');
  expect(writes()[1][1]).toEqual({ roundId: '1791252000', expectedRevision: 4, knightIndex: null });
});
it.each(['QuarterFinal', 'SemiFinal', 'Final', 'Result', 'Inactive'])('阶段%s只读，不能设置或取消', async phase => {
  data.phase = phase; data.canSetChampion = false; data.forcedChampionIndex = 1;
  render(<ConfigProvider><App><JoustCurrentRound onExpired={expired} onDenied={denied} onWriting={writing} /></App></ConfigProvider>);
  await screen.findByText('白骑士0（0）'); expect(screen.getAllByRole('radio').every(r => (r as HTMLInputElement).disabled)).toBe(true);
  expect((screen.getByRole('button', { name: '设为本场冠军' }) as HTMLButtonElement).disabled).toBe(true);
  expect((screen.getByRole('button', { name: '取消内定' }) as HTMLButtonElement).disabled).toBe(true); expect(writes()).toHaveLength(0);
});
it.each([409, 500])('保存失败%i不会因自动轮询而解锁，需要明确重读核对', async status => {
  let poll!: () => void; const interval = capturePoll(callback => { poll = callback; });
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation((request, payload) => payload ? Promise.reject(new ApiError(status, '提交待核对')) : original(request, payload));
  await open(); fireEvent.click(screen.getAllByRole('radio')[2]); fireEvent.click(screen.getByRole('button', { name: '设为本场冠军' })); fireEvent.click(await screen.findByRole('button', { name: '就它了' }));
  await screen.findByText('请重新读取当前场次后再操作');
  await act(async () => poll()); expect((screen.getByRole('button', { name: '设为本场冠军' }) as HTMLButtonElement).disabled).toBe(true);
  fireEvent.click(screen.getByRole('button', { name: '重新读取当前场次' }));
  await waitFor(() => expect((screen.getAllByRole('radio')[0] as HTMLInputElement).disabled).toBe(false));
  expect(screen.getAllByRole('radio').every(r => !(r as HTMLInputElement).checked)).toBe(true); expect(writes()).toHaveLength(1); interval.mockRestore();
});
it('轮询遇到外部版本变化，原选项不能覆盖；下一场清除选项', async () => {
  let poll!: () => void; const interval = capturePoll(callback => { poll = callback; });
  await open(); fireEvent.click(screen.getAllByRole('radio')[2]); data.revision++;
  await act(async () => poll()); await screen.findByText('场次版本已更新，请重新读取后选择骑士。');
  expect((screen.getByRole('button', { name: '设为本场冠军' }) as HTMLButtonElement).disabled).toBe(true);
  data.roundId = '1791259200'; data.revision = 0; await act(async () => poll());
  expect(screen.getAllByRole('radio').every(r => !(r as HTMLInputElement).checked)).toBe(true); expect(writes()).toHaveLength(0); interval.mockRestore();
});
