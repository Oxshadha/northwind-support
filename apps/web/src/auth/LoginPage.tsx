import { useState } from 'react';
import { Navigate, useLocation, useNavigate, type Location } from 'react-router-dom';
import { toErrorMessage } from '../api/client';
import { Field } from '../components/Field';
import { useAuth } from './AuthContext';

interface LoginForm {
  email: string;
  password: string;
}

type LoginErrors = Partial<Record<keyof LoginForm, string>>;

// Deliberately loose: the server decides what is a real account; this only catches typos.
const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

function validate(form: LoginForm): LoginErrors {
  const errors: LoginErrors = {};

  if (form.email.trim() === '') {
    errors.email = 'Enter your email address.';
  } else if (!emailPattern.test(form.email.trim())) {
    errors.email = 'Enter an email address like name@example.com.';
  }

  if (form.password === '') {
    errors.password = 'Enter your password.';
  }

  return errors;
}

/** Agents sign in here; afterwards they go back to the page they asked for. */
export function LoginPage() {
  const { agent, signIn } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [form, setForm] = useState<LoginForm>({ email: '', password: '' });
  const [errors, setErrors] = useState<LoginErrors>({});
  const [submitError, setSubmitError] = useState<string>();
  const [isSigningIn, setIsSigningIn] = useState(false);

  const from = (location.state as { from?: Location } | null)?.from;
  const destination = from ? `${from.pathname}${from.search}${from.hash}` : '/tickets';

  if (agent) {
    return <Navigate to={destination} replace />;
  }

  const submit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    const validationErrors = validate(form);
    setErrors(validationErrors);
    setSubmitError(undefined);

    if (Object.keys(validationErrors).length > 0) {
      return;
    }

    setIsSigningIn(true);

    try {
      await signIn(form.email.trim(), form.password);
      void navigate(destination, { replace: true });
    } catch (caught) {
      // The server gives one message for every failure, so it never says which part was wrong.
      setSubmitError(toErrorMessage(caught, 'Could not sign in.'));
      setForm((current) => ({ ...current, password: '' }));
    } finally {
      setIsSigningIn(false);
    }
  };

  return (
    <main className="login">
      <h1>Northwind Support</h1>
      <p className="page-header__subtitle">Sign in with your agent account.</p>

      <form className="card form" onSubmit={submit} noValidate aria-busy={isSigningIn}>
        <Field id="login-email" label="Email" error={errors.email}>
          {(fieldProps) => (
            <input
              {...fieldProps}
              type="email"
              autoComplete="username"
              placeholder="name@northwind-support.example"
              value={form.email}
              onChange={(event) => setForm((current) => ({ ...current, email: event.target.value }))}
            />
          )}
        </Field>

        <Field id="login-password" label="Password" error={errors.password}>
          {(fieldProps) => (
            <input
              {...fieldProps}
              type="password"
              autoComplete="current-password"
              value={form.password}
              onChange={(event) => setForm((current) => ({ ...current, password: event.target.value }))}
            />
          )}
        </Field>

        {submitError && (
          <p className="field__error panel__intro" role="alert">
            {submitError}
          </p>
        )}

        <button type="submit" className="button button--primary" disabled={isSigningIn}>
          {isSigningIn ? 'Signing in...' : 'Sign in'}
        </button>
      </form>
    </main>
  );
}
