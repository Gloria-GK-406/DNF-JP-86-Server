import { useState } from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import { expect, it, vi } from 'vitest';
import { api } from './api';
import { NotiHint, useNotiBar } from './NotiBar';
import { useGmRead } from './useGmRead';

vi.mock('./api', async original => ({ ...await original<typeof import('./api')>(), api: vi.fn() }));
const config = { top: 80, maxCount: 3 };
function Host({ children }: { children: React.ReactNode }) {
  return <ConfigProvider theme={{ token: { motion: false } }}><App notification={config}>{children}</App></ConfigProvider>;
}
function Controls() {
  const noti = useNotiBar();
  const [sequence, setSequence] = useState(0);
  return <section aria-label="页面内容">
    <button onClick={() => noti.error('首次错误', { key: 'same-read' })}>首次</button>
    <button onClick={() => noti.error('更新后的错误', { key: 'same-read' })}>更新</button>
    <button onClick={() => { noti.error(`错误 ${sequence}`); setSequence(sequence + 1); }}>新增</button>
    <button onClick={noti.clear}>清除通知</button>
  </section>;
}

it('通知位于全局容器，同编号更新且最多保留三条，清除后不残留', async () => {
  render(<Host><Controls /></Host>);
  fireEvent.click(screen.getByText('首次')); await screen.findByText('首次错误');
  expect(screen.getByText('首次错误').closest('section')).toBeNull();
  fireEvent.click(screen.getByText('更新')); await screen.findByText('更新后的错误');
  expect(screen.queryByText('首次错误')).toBeNull();
  expect(document.querySelectorAll('.gm-notibar')).toHaveLength(1);
  for (let index = 0; index < 4; index++) {
    fireEvent.click(screen.getByText('新增')); await screen.findByText(`错误 ${index}`);
  }
  expect(document.querySelectorAll('.gm-notibar')).toHaveLength(3);
  expect(screen.queryByText('错误 0')).toBeNull();
  expect(screen.queryByText('更新后的错误')).toBeNull();
  fireEvent.click(screen.getByText('清除通知'));
  await waitFor(() => expect(document.querySelectorAll('.gm-notibar')).toHaveLength(0));
});

it('待确认通知调用最新查询动作，所属状态消失时关闭', async () => {
  const first = vi.fn(); const latest = vi.fn();
  const view = render(<Host><NotiHint noticeKey="pending-operation" title="结果待确认" action={{ label: '查询结果', onClick: first }} /></Host>);
  await screen.findByText('结果待确认');
  view.rerender(<Host><NotiHint noticeKey="pending-operation" title="结果待确认" action={{ label: '查询结果', onClick: latest }} /></Host>);
  fireEvent.click(screen.getByRole('button', { name: '查询结果' }));
  expect(first).not.toHaveBeenCalled(); expect(latest).toHaveBeenCalledOnce();
  expect(document.querySelectorAll('.gm-notibar')).toHaveLength(1);
  view.rerender(<Host><div>已确认</div></Host>);
  await waitFor(() => expect(screen.queryByText('结果待确认')).toBeNull());
});

it('切换读取目标后旧请求失败不弹通知，当前失败只弹一次', async () => {
  let rejectOld!: (failure: unknown) => void;
  const old = new Promise((_, reject) => { rejectOld = reject; });
  vi.mocked(api).mockImplementation(path => path === 'old' ? old : Promise.reject(new Error('当前读取失败')));
  const expired = vi.fn(); const denied = vi.fn();
  function Read({ path }: { path: string }) { useGmRead(path, expired, denied); return <div>{path}</div>; }
  const view = render(<Host><Read path="old" /></Host>);
  view.rerender(<Host><Read path="current" /></Host>);
  await screen.findByText('当前读取失败');
  await act(async () => { rejectOld(new Error('旧读取失败')); });
  expect(screen.queryByText('旧读取失败')).toBeNull();
  expect(document.querySelectorAll('.gm-notibar')).toHaveLength(1);
  expect(expired).not.toHaveBeenCalled(); expect(denied).not.toHaveBeenCalled();
});
