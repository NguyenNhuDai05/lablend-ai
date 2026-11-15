import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { configureApiClient } from '../lib/api';
import { authApi } from './auth-api';
import type { CurrentUser, LoginRequest, RegisterRequest } from './types';

const tokenKey = 'lablend.access-token';
type AuthContextValue = {
  user: CurrentUser | null;
  status: 'loading' | 'authenticated' | 'anonymous';
  login: (request: LoginRequest) => Promise<void>;
  register: (request: RegisterRequest) => Promise<void>;
  logout: () => Promise<void>;
};
const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [status, setStatus] = useState<AuthContextValue['status']>('loading');
  const clearSession = useCallback(() => {
    sessionStorage.removeItem(tokenKey);
    setUser(null);
    setStatus('anonymous');
  }, []);

  useEffect(() => {
    configureApiClient({ getAccessToken: () => sessionStorage.getItem(tokenKey), onUnauthorized: clearSession });
    if (!sessionStorage.getItem(tokenKey)) { setStatus('anonymous'); return; }
    void authApi.currentUser()
      .then(current => { setUser(current); setStatus('authenticated'); })
      .catch(clearSession);
  }, [clearSession]);

  const value = useMemo<AuthContextValue>(() => ({
    user, status,
    async login(request) {
      const response = await authApi.login(request);
      sessionStorage.setItem(tokenKey, response.accessToken);
      setUser(response.user);
      setStatus('authenticated');
    },
    async register(request) { await authApi.register(request); },
    async logout() { try { await authApi.logout(); } finally { clearSession(); } },
  }), [clearSession, status, user]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used inside AuthProvider.');
  return context;
}
