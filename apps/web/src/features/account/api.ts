import { apiRequest } from '../../lib/api';
import type { CurrentUserResponse, UserRole } from '../../auth/types';
import type { Paged } from '../catalog/api';
export const accountApi = {
  me: (signal?: AbortSignal) => apiRequest<CurrentUserResponse>('/api/users/me', { signal }),
  updateProfile: (displayName: string) => apiRequest<CurrentUserResponse>('/api/users/me', { method: 'PUT', body: JSON.stringify({ displayName: displayName.trim() }) }),
  changePassword: (currentPassword: string, newPassword: string) => apiRequest<void>('/api/users/me/change-password', { method: 'POST', body: JSON.stringify({ currentPassword, newPassword }) }),
  list: (page: number, signal?: AbortSignal) => apiRequest<Paged<CurrentUserResponse>>(`/api/users?page=${page}&pageSize=20`, { signal }),
  detail: (id: string, signal?: AbortSignal) => apiRequest<CurrentUserResponse>(`/api/users/${encodeURIComponent(id)}`, { signal }),
  role: (id: string, role: UserRole) => apiRequest<CurrentUserResponse>(`/api/users/${encodeURIComponent(id)}/role`, { method: 'PUT', body: JSON.stringify({ role }) }),
  active: (id: string, isActive: boolean) => apiRequest<CurrentUserResponse>(`/api/users/${encodeURIComponent(id)}/active`, { method: 'PATCH', body: JSON.stringify({ isActive }) }),
};
