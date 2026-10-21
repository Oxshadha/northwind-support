import type { Session } from '../types/api';

/**
 * Where the signed-in session lives: sessionStorage, so it survives a page refresh but not the
 * tab closing, and is not shared with other tabs. Like any script-readable storage it is
 * exposed to XSS; see the README for the trade-off and what production would do instead.
 */
const storageKey = 'northwind.session';

export function readSession(now: Date = new Date()): Session | undefined {
  try {
    const raw = window.sessionStorage.getItem(storageKey);
    const session = raw ? (JSON.parse(raw) as Session) : undefined;

    // An expired token would only earn a 401; treat it as signed out straight away.
    if (session && new Date(session.expiresAtUtc) <= now) {
      clearSession();
      return undefined;
    }

    return session;
  } catch {
    // Storage blocked or the value is corrupt: behave as signed out.
    return undefined;
  }
}

export function writeSession(session: Session): void {
  try {
    window.sessionStorage.setItem(storageKey, JSON.stringify(session));
  } catch {
    // Storage blocked: the session still works in memory until the page reloads.
  }
}

export function clearSession(): void {
  try {
    window.sessionStorage.removeItem(storageKey);
  } catch {
    // Nothing stored, nothing to clear.
  }
}
