import { render, screen, fireEvent } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import zhCN from 'antd/locale/zh_CN';
import { MemoryRouter } from 'react-router-dom';
import { AdminApp } from './AdminApp';
import { LookProvider } from './look';

/** Renders the whole console at a route, the way an operator lands on it. */
export function renderApp(path: string, locale = true) {
  return render(<LookProvider><ConfigProvider locale={locale ? zhCN : undefined} theme={{ token: { motion: false } }}><App>
    <MemoryRouter initialEntries={[path]}><AdminApp /></MemoryRouter></App></ConfigProvider></LookProvider>);
}
export function deferred<T>() {
  let resolve!: (value: T) => void; let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((succeed, fail) => { resolve = succeed; reject = fail; });
  return { promise, resolve, reject };
}
/** The account chip opens the session popover that holds 退出登录. */
export async function logout() {
  fireEvent.click(await screen.findByRole('button', { name: '账号与会话' }));
  fireEvent.click(await screen.findByRole('button', { name: /退出登录/ }));
}
/** Shell side reads that tolerate failure; tests that count requests ignore them. */
export const shellPaths = new Set(['server-info', 'items/icon-pack', 'overview']);
