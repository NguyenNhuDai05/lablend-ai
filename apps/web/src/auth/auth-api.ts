import { apiRequest } from '../lib/api';
import type { AuthResponse, CurrentUser, LoginRequest, RegisterRequest } from './types';

export const authApi = {
  login: (request: LoginRequest) => apiRequest<AuthResponse>('/api/auth/login', { method: 'POST', body: JSON.stringify(request) }),
  register: (request: RegisterRequest) => apiRequest<CurrentUser>('/api/auth/register', { method: 'POST', body: JSON.stringify(request) }),
  currentUser: () => apiRequest<CurrentUser>('/api/auth/me'),
  logout: () => apiRequest<void>('/api/auth/logout', { method: 'POST' }),
};
