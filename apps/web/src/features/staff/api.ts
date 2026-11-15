import { useCallback, useEffect, useState } from 'react';
import { apiBaseUrl } from '../../lib/api';

// DTOs mirror docs/api/REST_CONTRACTS.md (Catalog v1, Inventory v1).
export type Paged<T> = { items: T[]; total: number; page: number; pageSize: number };
export type Category = { id: string; name: string; description: string | null; isActive: boolean; createdAt: string };
export type EquipmentType = {
  id: string; categoryId: string; categoryName: string; name: string; description: string | null;
  specifications: string | null; tags: string[]; imageUrl: string | null; isActive: boolean; createdAt: string;
};
export const assetStatuses = ['AVAILABLE', 'RESERVED', 'ON_LOAN', 'MAINTENANCE', 'LOST', 'RETIRED'] as const;
export type AssetStatus = typeof assetStatuses[number];
export type Asset = {
  id: string; equipmentTypeId: string; serialNumber: string; location: string; notes: string | null;
  status: AssetStatus; createdAt: string; updatedAt: string;
};
export type Maintenance = {
  id: string; reason: string; openedAt: string; completedAt: string | null; completionNotes: string | null;
};
export type Availability = { equipmentTypeId: string; available: number; total: number };

export class ApiError extends Error {
  constructor(readonly status: number, message: string) { super(message); }
}

const tokenKey = 'lablend.staffToken';
// ponytail: dev token in sessionStorage until M3's auth client + M1's Identity login exist; then read the token from there.
export const session = {
  get: () => { try { return sessionStorage.getItem(tokenKey); } catch { return null; } },
  set: (token: string | null) => {
    try { token ? sessionStorage.setItem(tokenKey, token) : sessionStorage.removeItem(tokenKey); } catch { /* private mode */ }
  }
};

export type TokenInfo = { role: string; expiresAt: Date };
export function readToken(token: string): TokenInfo | null {
  try {
    const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')));
    return { role: String(payload.role ?? ''), expiresAt: new Date(Number(payload.exp) * 1000) };
  } catch {
    return null;
  }
}

let onUnauthorized: () => void = () => {};
export const setUnauthorizedHandler = (handler: () => void) => { onUnauthorized = handler; };

export async function api<T = void>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const token = session.get();
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(body ? { 'Content-Type': 'application/json' } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(10000)
  });
  if (response.status === 204) return undefined as T;
  const data = await response.json().catch(() => null);
  if (response.ok) return data as T;
  if (response.status === 401) onUnauthorized();
  throw new ApiError(response.status, problemMessage(response.status, data));
}

function problemMessage(status: number, problem: { detail?: string; title?: string; errors?: Record<string, string[]> } | null) {
  if (status === 401) return 'Phiên đăng nhập hết hạn hoặc token không hợp lệ.';
  if (status === 403) return 'Tài khoản không có quyền LAB_STAFF/ADMIN.';
  if (status === 429) return 'Quá nhiều yêu cầu, thử lại sau một phút.';
  const fields = problem?.errors ? Object.values(problem.errors).flat().join(' ') : '';
  return fields || problem?.detail || problem?.title || `Máy chủ trả HTTP ${status}.`;
}

export const query = (params: Record<string, string | number | boolean | undefined>) =>
  new URLSearchParams(Object.entries(params).filter(([, v]) => v !== undefined && v !== '').map(([k, v]) => [k, String(v)])).toString();

/** Loads on mount and whenever `key` changes; `reload` refetches. */
export function useLoad<T>(load: () => Promise<T>, key: string) {
  const [state, setState] = useState<{ data?: T; error?: string; loading: boolean }>({ loading: true });
  const [version, setVersion] = useState(0);
  useEffect(() => {
    let alive = true;
    setState(s => ({ ...s, loading: true }));
    load().then(data => alive && setState({ data, loading: false }),
      (e: unknown) => alive && setState({ error: e instanceof Error ? e.message : 'Không tải được dữ liệu.', loading: false }));
    return () => { alive = false; };
  }, [key, version]); // eslint-disable-line react-hooks/exhaustive-deps
  return { ...state, reload: useCallback(() => setVersion(v => v + 1), []) };
}
