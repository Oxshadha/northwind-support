import { createContext, useContext } from 'react';
import type { CurrentAgent } from '../types/api';

export interface AuthContextValue {
  agent?: CurrentAgent;
  signIn: (email: string, password: string) => Promise<void>;
  signOut: () => void;
}

export const AuthContext = createContext<AuthContextValue | undefined>(undefined);

/** The signed-in agent and the sign-in/out actions. Only valid inside AuthProvider. */
export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);

  if (!value) {
    throw new Error('useAuth must be used inside AuthProvider.');
  }

  return value;
}
