import type { ProblemDetails } from '../types/api';

/**
 * Where the API lives. In development this stays '/api' and the Vite dev server proxies it
 * to the backend; set VITE_API_BASE_URL to point a build somewhere else. See .env.example.
 */
const baseUrl = import.meta.env.VITE_API_BASE_URL ?? '/api';

/** A failed HTTP call, carrying the problem document the API returned. */
export class ApiError extends Error {
  readonly status: number;

  readonly problem?: ProblemDetails;

  constructor(status: number, message: string, problem?: ProblemDetails) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }

  /** Field name to messages, when the failure was a validation failure. */
  get fieldErrors(): Record<string, string[]> {
    return this.problem?.errors ?? {};
  }
}

interface AuthHooks {
  /** The current access token, if anyone is signed in. */
  getToken: () => string | undefined;
  /** Called when the API rejects a token we sent: the session is over. */
  onUnauthorized: () => void;
}

let auth: AuthHooks = { getToken: () => undefined, onUnauthorized: () => undefined };

/** Connects the client to the session, so every call carries the token. */
export function configureAuth(hooks: AuthHooks): void {
  auth = hooks;
}

/**
 * The single place that talks to the API. Attaches the signed-in agent's token, returns
 * parsed JSON, or throws an {@link ApiError} that callers can show to the user.
 */
export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const token = auth.getToken();

  const response = await fetch(`${baseUrl}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(init?.headers ?? {}),
    },
  });

  // A token we sent was refused (expired, or the agent deactivated): end the session. A 401
  // without a token - a failed sign-in - is just an error for the caller to show.
  if (response.status === 401 && token) {
    auth.onUnauthorized();
  }

  if (!response.ok) {
    const problem = await readProblem(response);

    throw new ApiError(
      response.status,
      problem?.detail ?? problem?.title ?? response.statusText,
      problem,
    );
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

/** Turns anything thrown by a fetch into a message worth showing a user. */
export function toErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof ApiError) {
    return error.message;
  }

  if (error instanceof Error) {
    return `${fallback} (${error.message})`;
  }

  return fallback;
}

async function readProblem(response: Response): Promise<ProblemDetails | undefined> {
  try {
    return (await response.json()) as ProblemDetails;
  } catch {
    // Not every failure has a JSON body - a dead server certainly will not.
    return undefined;
  }
}
