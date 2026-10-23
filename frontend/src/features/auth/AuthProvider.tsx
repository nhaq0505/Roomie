import { useQueryClient } from '@tanstack/react-query';
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { ApiError } from '../../shared/http/client';
import { authApi } from './api';
import type { User } from './types';

interface AuthValue {
  user: User | null;
  ready: boolean;
  error: Error | null;
  setUser: (user: User | null) => void;
  patchUser: (patch: Partial<User>) => void;
  refresh: () => Promise<User | null>;
  signOut: () => Promise<void>;
}

const AuthContext = createContext<AuthValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUserState] = useState<User | null>(null);
  const [error, setError] = useState<Error | null>(null);
  const [ready, setReady] = useState(false);
  const queryClient = useQueryClient();
  const generation = useRef(0);
  const setUser = useCallback((next: User | null) => {
    generation.current++;
    void queryClient.cancelQueries();
    queryClient.clear(); setError(null); setUserState(next); setReady(true);
  }, [queryClient]);
  const patchUser = useCallback((patch: Partial<User>) => {
    setUserState((prev) => (prev ? { ...prev, ...patch } : prev));
  }, []);
  const refresh = useCallback(async () => {
    const request = ++generation.current;
    setError(null);
    try {
      const next = await authApi.me();
      if (request !== generation.current) return null;
      void queryClient.cancelQueries();
      queryClient.clear(); setUserState(next);
      return next;
    } catch (error) {
      if (request !== generation.current) return null;
      if (error instanceof ApiError && error.status === 401) {
        void queryClient.cancelQueries();
        queryClient.clear(); setUserState(null);
      }
      else setError(error instanceof Error ? error : new Error(String(error)));
      return null;
    } finally {
      if (request === generation.current) setReady(true);
    }
  }, [queryClient]);
  const signOut = useCallback(async () => {
    void queryClient.cancelQueries();
    await authApi.logout(); setUser(null);
  }, [queryClient, setUser]);
  useEffect(() => {
    if (/^\/(?:t|tenant)\/[^/]+/.test(window.location.pathname)) { setReady(true); return; }
    void refresh();
  }, [refresh]);
  const value = useMemo(() => ({ user, ready, error, setUser, patchUser, refresh, signOut }), [user, ready, error, setUser, patchUser, refresh, signOut]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const value = useContext(AuthContext);
  if (!value) throw new Error('useAuth must be used within AuthProvider');
  return value;
}
