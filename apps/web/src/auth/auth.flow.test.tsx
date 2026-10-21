import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../api/client';
import { authApi } from '../api/auth';
import { routes } from '../routes';
import { clearSession } from './session';

vi.mock('../api/auth', () => ({ authApi: { login: vi.fn(), me: vi.fn() } }));

const login = vi.mocked(authApi.login);

const signedIn = {
  token: 'signed.jwt.token',
  expiresAtUtc: new Date(Date.now() + 8 * 3600_000).toISOString(),
  agent: { id: 4, fullName: 'Sara Lindqvist', email: 'sara.lindqvist@northwind-support.example' },
};

// A protected page that fetches nothing, so the test is only about getting there.
const renderAt = (path: string) => {
  const router = createMemoryRouter(routes, { initialEntries: [path] });
  render(<RouterProvider router={router} />);
  return router;
};

describe('signing in', () => {
  beforeEach(() => clearSession());
  afterEach(() => vi.clearAllMocks());

  it('sends a visitor to /login, then back to the page they wanted', async () => {
    login.mockResolvedValue(signedIn);
    const router = renderAt('/no-such-page?tab=2');

    expect(await screen.findByRole('heading', { name: 'Northwind Support' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/login');

    await userEvent.type(screen.getByLabelText('Email'), 'sara.lindqvist@northwind-support.example');
    await userEvent.type(screen.getByLabelText('Password'), 'LocalDev-Only-2026!');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByText('Signed in as')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/no-such-page');
    expect(router.state.location.search).toBe('?tab=2');
    expect(screen.getByText('Sara Lindqvist')).toBeInTheDocument();
  });

  it('checks the form before calling the API', async () => {
    renderAt('/login');

    await userEvent.type(await screen.findByLabelText('Email'), 'not-an-email');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(screen.getByText('Enter an email address like name@example.com.')).toBeInTheDocument();
    expect(screen.getByText('Enter your password.')).toBeInTheDocument();
    expect(login).not.toHaveBeenCalled();
  });

  it('shows the server refusal, clears the password, and stays on /login', async () => {
    login.mockRejectedValue(new ApiError(401, 'Email or password is incorrect.'));
    const router = renderAt('/login');

    await userEvent.type(await screen.findByLabelText('Email'), 'sara.lindqvist@northwind-support.example');
    await userEvent.type(screen.getByLabelText('Password'), 'wrong');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Email or password is incorrect.');
    expect(screen.getByLabelText('Password')).toHaveValue('');
    expect(router.state.location.pathname).toBe('/login');
  });

  it('signs out from the header, back to /login', async () => {
    login.mockResolvedValue(signedIn);
    const router = renderAt('/login');

    await userEvent.type(await screen.findByLabelText('Email'), 'sara.lindqvist@northwind-support.example');
    await userEvent.type(screen.getByLabelText('Password'), 'LocalDev-Only-2026!');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Sign out' }));

    expect(router.state.location.pathname).toBe('/login');
    expect(window.sessionStorage.length).toBe(0);
  });
});
