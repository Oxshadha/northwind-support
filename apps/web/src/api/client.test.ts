import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, configureAuth, request } from './client';

const fetchMock = vi.fn();

const respond = (status: number, body: unknown = {}) =>
  fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(body), { status }));

describe('request', () => {
  const onUnauthorized = vi.fn();

  beforeEach(() => {
    vi.stubGlobal('fetch', fetchMock);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.clearAllMocks();
    configureAuth({ getToken: () => undefined, onUnauthorized: () => undefined });
  });

  it('attaches the signed-in agent token to every call', async () => {
    configureAuth({ getToken: () => 'signed.jwt.token', onUnauthorized });
    respond(200, []);

    await request('/tickets');

    const headers = fetchMock.mock.calls[0][1].headers as Record<string, string>;
    expect(headers.Authorization).toBe('Bearer signed.jwt.token');
  });

  it('sends no Authorization header when nobody is signed in', async () => {
    respond(200, []);

    await request('/tickets');

    const headers = fetchMock.mock.calls[0][1].headers as Record<string, string>;
    expect(headers.Authorization).toBeUndefined();
  });

  it('ends the session when the API refuses a token it was sent', async () => {
    configureAuth({ getToken: () => 'expired.jwt.token', onUnauthorized });
    respond(401, { title: 'Unauthorized', detail: 'Sign in to use the API.' });

    await expect(request('/tickets')).rejects.toBeInstanceOf(ApiError);
    expect(onUnauthorized).toHaveBeenCalledOnce();
  });

  it('treats a 401 without a token (a failed sign-in) as an ordinary error', async () => {
    configureAuth({ getToken: () => undefined, onUnauthorized });
    respond(401, { detail: 'Email or password is incorrect.' });

    await expect(request('/auth/login', { method: 'POST' })).rejects.toThrow('Email or password is incorrect.');
    expect(onUnauthorized).not.toHaveBeenCalled();
  });
});
