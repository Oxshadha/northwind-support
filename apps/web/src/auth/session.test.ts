import { afterEach, describe, expect, it } from 'vitest';
import { clearSession, readSession, writeSession } from './session';

const session = (expiresAtUtc: string) => ({
  token: 'signed.jwt.token',
  expiresAtUtc,
  agent: { id: 4, fullName: 'Sara Lindqvist', email: 'sara.lindqvist@northwind-support.example' },
});

describe('session storage', () => {
  afterEach(() => clearSession());

  it('restores a session that has not expired', () => {
    writeSession(session('2026-10-21T18:00:00Z'));

    expect(readSession(new Date('2026-10-21T17:59:00Z'))?.agent.fullName).toBe('Sara Lindqvist');
  });

  it('drops an expired session instead of restoring it', () => {
    writeSession(session('2026-10-21T18:00:00Z'));

    expect(readSession(new Date('2026-10-21T18:00:00Z'))).toBeUndefined();
    expect(window.sessionStorage.length).toBe(0);
  });

  it('treats a corrupt value as signed out', () => {
    window.sessionStorage.setItem('northwind.session', '{not json');

    expect(readSession()).toBeUndefined();
  });
});
