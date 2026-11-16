import { useCallback, useEffect, useState } from 'react';
import { apiRequest } from '../../lib/api';

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
/** fromStatus null = first known status; loanId set for Borrowing (gRPC) changes, changedBy for staff (REST) changes. */
export type StockMovement = {
  id: number; fromStatus: AssetStatus | null; toStatus: AssetStatus; loanId: string | null; changedBy: string | null; occurredAt: string;
};
export type Availability = { equipmentTypeId: string; available: number; total: number };

export async function api<T = void>(path: string, method = 'GET', body?: unknown): Promise<T> {
  return apiRequest<T>(path, {
    method,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
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
