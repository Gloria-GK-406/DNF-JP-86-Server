import { useEffect, useRef, useState } from 'react';
import { api, ApiError } from './api';

export function useGmRead<T>(path: string | null, onExpired: () => void, onDenied: () => void) {
  const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<{ key: string; data?: T; error?: string; busy: boolean }>({ key: '', busy: false });
  const key = `${path}:${revision}`;
  const current = useRef(key); current.current = key;
  const expired = useRef(onExpired); expired.current = onExpired;
  const denied = useRef(onDenied); denied.current = onDenied;
  useEffect(() => {
    if (!path) return;
    let active = true;
    setResult({ key, busy: true });
    api<T>(path).then(data => { if (active && current.current === key) setResult({ key, data, busy: false }); }).catch(failure => {
      if (!active || current.current !== key) return;
      setResult({ key, error: (failure as Error).message, busy: false });
      if (failure instanceof ApiError && failure.status === 401) expired.current();
      if (failure instanceof ApiError && failure.status === 403) denied.current();
    });
    return () => { active = false; };
  }, [path, revision]);
  return { data: result.key === key ? result.data : undefined, error: result.key === key ? result.error : undefined,
    busy: !!path && (result.key !== key || result.busy), refresh: () => setRevision(value => value + 1) };
}
