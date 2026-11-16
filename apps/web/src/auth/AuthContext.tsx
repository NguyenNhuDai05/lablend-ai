import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { configureApiClient } from '../lib/api';
import { authApi } from './auth-api';
import type { CurrentUser, LoginRequest, RegisterRequest } from './types';

function removeLegacyToken() {
  try { sessionStorage.removeItem('lablend.access-token'); } catch { /* Storage may be disabled; tokens stay in memory. */ }
}
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
  const accessToken = useRef<string | null>(null);
  const generation = useRef(0);
  const refreshing = useRef<Promise<void> | null>(null);
  const clearSession = useCallback(() => {
    generation.current++;
    accessToken.current = null;
    removeLegacyToken();
    setUser(null);
    setStatus('anonymous');
  }, []);
  const refreshSession = useCallback(() => {
    if (!refreshing.current) {
      const current = generation.current;
      refreshing.current = authApi.refresh().then(response => {
        if (current !== generation.current) return;
        accessToken.current = response.accessToken;
        setUser(response.user);
        setStatus('authenticated');
      }).finally(() => { refreshing.current = null; });
    }
    return refreshing.current;
  }, []);

  useEffect(() => {
    removeLegacyToken();
    configureApiClient({ getAccessToken: () => accessToken.current, getSessionVersion: () => generation.current,
      onUnauthorized: clearSession, refreshAccessToken: refreshSession });
    const current = generation.current;
    void refreshSession().catch(() => { if (current === generation.current) clearSession(); });
  }, [clearSession, refreshSession]);

  const value = useMemo<AuthContextValue>(() => ({
    user, status,
    async login(request) {
      const current = ++generation.current;
      const response = await authApi.login(request).catch(error => {
        if (current === generation.current && !accessToken.current) setStatus('anonymous');
        throw error;
      });
      if (current !== generation.current) return;
      accessToken.current = response.accessToken;
      setUser(response.user);
      setStatus('authenticated');
    },
    async register(request) { await authApi.register(request); },
    async logout() { clearSession(); try { await authApi.logout(); } finally { clearSession(); } },
  }), [clearSession, status, user]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used inside AuthProvider.');
  return context;
}
