import { api } from './api';

/** Reads that the shell and item widgets share; failures stay silent because every caller has a fallback. */
export async function quietApi<T>(path: string): Promise<T | undefined> {
  try { return await api<T>(path); } catch { return undefined; }
}
