import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import zhCN from 'antd/locale/zh_CN';
import { MemoryRouter } from 'react-router-dom';
import { AdminApp } from './AdminApp';
import { Portal } from './Portal';
import { LookProvider } from './look';
import { expect, vi } from 'vitest';

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
/** antd drops a button's loading state a macrotask after the read settles and ignores clicks until then. */
export async function clickWhenIdle(button: () => HTMLElement) {
  await waitFor(() => expect(button().className).not.toContain('ant-btn-loading'));
  fireEvent.click(button());
}
/** Shell side reads that tolerate failure; tests that count requests ignore them. */
export const shellPaths = new Set(['server-info', 'items/icon-pack', 'overview']);
/** Renders the top-level shell (player portal + administrator console) at a route. */
export function renderPortal(path = '/') {
  return render(<LookProvider><ConfigProvider locale={zhCN} theme={{ token: { motion: false } }}><App>
    <MemoryRouter initialEntries={[path]}><Portal /></MemoryRouter></App></ConfigProvider></LookProvider>);
}
export type PlayerCall = { method: string; path: string; body: unknown; csrf: string | null };
export type PlayerReply = { status?: number; body?: unknown } | undefined;
/** Fakes the player API at the fetch level, so playerApi's own parsing and code branching run. `path` has no prefix
    and keeps its query string; auth/csrf answers itself. An undefined reply is 404. Thrown errors reach fetch's caller. */
export function stubPlayerFetch(handler: (call: PlayerCall) => PlayerReply | Promise<PlayerReply>) {
  const calls: PlayerCall[] = [];
  const fake = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    if (!url.startsWith('/api/player/v1/')) throw new Error(`Unexpected fetch ${url}`);
    const path = url.slice('/api/player/v1/'.length);
    const headers = (init?.headers ?? {}) as Record<string, string>;
    const call = { method: init?.method ?? 'GET', path, body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined, csrf: headers['X-GM-CSRF'] ?? null };
    if (path === 'auth/csrf') return new Response(JSON.stringify({ token: 'player-token' }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    calls.push(call);
    const reply = await handler(call) ?? { status: 404, body: { message: '未找到。', code: 'not_found' } };
    const status = reply.status ?? 200;
    return new Response(status === 204 ? null : typeof reply.body === 'string' ? reply.body : JSON.stringify(reply.body ?? {}),
      { status, headers: { 'Content-Type': 'application/json' } });
  });
  vi.stubGlobal('fetch', fake);
  return calls;
}
export const refuse = (status: number, code: string | undefined, message = '拒绝。', outcome?: string) => ({ status, body: { message, ...(code ? { code } : {}), ...(outcome ? { outcome } : {}) } });
