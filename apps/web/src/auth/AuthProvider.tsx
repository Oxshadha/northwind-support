import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { authApi } from '../api/auth';
import { configureAuth } from '../api/client';
import type { Session } from '../types/api';
import { AuthContext, type AuthContextValue } from './AuthContext';
import { clearSession, readSession, writeSession } from './session';

/** Owns the session: restores it on load, signs in and out, and ends it on any 401. */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | undefined>(() => readSession());
  const navigate = useNavigate();

  const signOut = useCallback(() => {
    clearSession();
    setSession(undefined);
    void navigate('/login', { replace: true });
  }, [navigate]);

  // Every API call carries the token; a 401 on one (expired or revoked) ends the session.
  useEffect(() => {
    configureAuth({ getToken: () => readSession()?.token, onUnauthorized: signOut });
  }, [signOut]);

  // Sign out the moment the token expires, rather than on the next failed call.
  useEffect(() => {
    if (!session) {
      return;
    }

    const remaining = new Date(session.expiresAtUtc).getTime() - Date.now();
    const handle = window.setTimeout(signOut, Math.max(remaining, 0));

    return () => window.clearTimeout(handle);
  }, [session, signOut]);

  const signIn = useCallback(async (email: string, password: string) => {
    const response = await authApi.login(email, password);
    const next: Session = { token: response.token, expiresAtUtc: response.expiresAtUtc, agent: response.agent };

    writeSession(next);
    setSession(next);
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({ agent: session?.agent, signIn, signOut }),
    [session, signIn, signOut],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
