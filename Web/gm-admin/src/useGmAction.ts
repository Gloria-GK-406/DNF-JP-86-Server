import { useEffect, useRef, useState } from 'react';
import { App } from 'antd';
import { ApiError } from './api';
import { useNotiBar } from './NotiBar';

/** One write at a time per view; late results after unmount are dropped; 401 / 403 go to the shell. */
export function useGmAction({ onExpired, onDenied }: { onExpired: () => void; onDenied: () => void }) {
  const noti = useNotiBar();
  const [busy, setBusy] = useState(false);
  const epoch = useRef(0); const alive = useRef(true); const running = useRef(false);
  useEffect(() => { alive.current = true; return () => { alive.current = false; epoch.current++; }; }, []);
  async function run<T>(action: () => Promise<T>, accept: (value: T) => void, reject?: (failure: unknown) => boolean) {
    if (running.current) return;
    const current = ++epoch.current; running.current = true; setBusy(true);
    try { const value = await action(); if (alive.current && current === epoch.current) accept(value); }
    catch (failure) {
      if (!alive.current || current !== epoch.current) return;
      if (failure instanceof ApiError && failure.status === 401) onExpired();
      else if (failure instanceof ApiError && failure.status === 403) onDenied();
      else if (!reject?.(failure)) noti.failure(failure);
    } finally { if (alive.current && current === epoch.current) { running.current = false; setBusy(false); } }
  }
  return { busy, run };
}

/** Closing an editor with edits asks first. */
export function useConfirmLeave() {
  const { modal } = App.useApp();
  return (dirty: boolean, leave: () => void, text = { title: '放弃未保存的修改？', content: '关闭后将丢弃这次的修改。', ok: '放弃修改', cancel: '继续编辑' }) => {
    if (!dirty) { leave(); return; }
    modal.confirm({ title: text.title, content: text.content, okText: text.ok, cancelText: text.cancel, onOk: leave });
  };
}
