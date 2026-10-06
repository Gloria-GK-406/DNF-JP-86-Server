import { useEffect, useMemo, useRef } from 'react';
import { App, Button } from 'antd';

type Level = 'success' | 'info' | 'warning' | 'error';
export type NotiOptions = {
  key?: string;
  description?: string;
  persistent?: boolean;
  action?: { label: string; onClick: () => void };
};

export function failureMessage(failure: unknown): string {
  if (failure instanceof Error) {
    if (failure.name === 'TimeoutError' || failure.name === 'AbortError') return '请求超时，请稍后重试。';
    if (failure instanceof TypeError) return '无法连接管理服务，请检查连接后重试。';
    return failure.message;
  }
  return '操作未完成，请稍后重试。';
}

export function useNotiBar() {
  const { notification } = App.useApp();
  return useMemo(() => {
    function show(type: Level, title: string, options: NotiOptions = {}) {
      notification.open({ type, title, description: options.description, key: options.key,
        className: 'gm-notibar', placement: 'topRight',
        duration: options.persistent ? 0 : type === 'success' || type === 'info' ? 3 : 8,
        role: type === 'error' || type === 'warning' ? 'alert' : 'status',
        actions: options.action && <Button size="small" onClick={options.action.onClick}>{options.action.label}</Button> });
    }
    return {
      success: (title: string, options?: NotiOptions) => show('success', title, options),
      info: (title: string, options?: NotiOptions) => show('info', title, options),
      warning: (title: string, options?: NotiOptions) => show('warning', title, options),
      error: (title: string, options?: NotiOptions) => show('error', title, options),
      failure: (failure: unknown, key?: string) => show('error', failureMessage(failure), { key }),
      close: (key: string) => notification.destroy(key),
      clear: () => notification.destroy(),
    };
  }, [notification]);
}

/** A persistent state notification disappears when that state or its owning view disappears. */
export function NotiHint({ type = 'warning', title, noticeKey, description, action }: {
  type?: Level; title: string; noticeKey: string; description?: string; action?: NotiOptions['action'];
}) {
  const noti = useNotiBar();
  const currentAction = useRef(action); currentAction.current = action;
  const label = action?.label;
  useEffect(() => {
    noti[type](title, { key: noticeKey, description, persistent: true,
      action: label ? { label, onClick: () => currentAction.current?.onClick() } : undefined });
    return () => noti.close(noticeKey);
  }, [noti, type, title, noticeKey, description, label]);
  return null;
}
