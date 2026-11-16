import { apiRequest } from '../../lib/api';
import { query } from '../../lib/workflows';
import type { EquipmentType, Category, Availability, Paged } from '../staff/api';
export type { EquipmentType, Category, Availability, Paged };
export const catalogApi = {
  categories: (signal?: AbortSignal) => apiRequest<Category[]>('/api/categories', { signal }),
  list: (params: { keyword?: string; categoryId?: string; sortBy?: string; direction?: string; page?: number; pageSize?: number }, signal?: AbortSignal) => apiRequest<Paged<EquipmentType>>(`/api/equipment-types?${query(params)}`, { signal }),
  detail: (id: string, signal?: AbortSignal) => apiRequest<EquipmentType>(`/api/equipment-types/${encodeURIComponent(id)}`, { signal }),
  availability: (ids: string[], signal?: AbortSignal) => ids.length ? apiRequest<Availability[]>(`/api/inventory/availability?${ids.map(id => `equipmentTypeIds=${encodeURIComponent(id)}`).join('&')}`, { signal }) : Promise.resolve([]),
};
