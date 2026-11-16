import { apiRequest } from '../lib/api';
import { toCurrentUser } from './auth-user';
import type { AuthResponse, AuthSessionResponse, CurrentUserResponse, LoginRequest, RegisterRequest } from './types';

let cookieQueue: Promise<unknown> = Promise.resolve();
function sessionCommand<T>(work: () => Promise<T>): Promise<T> {
  const run = cookieQueue.then(async (): Promise<T> => {
    // Cookies are shared between tabs. Avoid concurrent rotation there when Web Locks is available.
    if (typeof navigator !== 'undefined' && navigator.locks) return await navigator.locks.request('lablend.session', work);
    return await work();
  });
  cookieQueue = run.catch(() => undefined);
  return run;
}
const session = (response: AuthSessionResponse): AuthResponse => ({ ...response, user: toCurrentUser(response.user) });

export const authApi = {
  login: (request: LoginRequest) => sessionCommand(() => apiRequest<AuthSessionResponse>('/api/auth/login', { method: 'POST', body: JSON.stringify(request) }).then(session)),
  register: (request: RegisterRequest) => apiRequest<CurrentUserResponse>('/api/auth/register', { method: 'POST', body: JSON.stringify(request) }).then(toCurrentUser),
  currentUser: () => apiRequest<CurrentUserResponse>('/api/auth/me').then(toCurrentUser),
  refresh: () => sessionCommand(() => apiRequest<AuthSessionResponse>('/api/auth/refresh', { method: 'POST' }).then(session)),
  logout: () => sessionCommand(() => apiRequest<void>('/api/auth/logout', { method: 'POST' })),
};
