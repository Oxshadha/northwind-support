import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from './AuthContext';

/** Sends a visitor who is not signed in to /login, remembering where they were going. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { agent } = useAuth();
  const location = useLocation();

  if (!agent) {
    return <Navigate to="/login" replace state={{ from: location }} />;
  }

  return children;
}
