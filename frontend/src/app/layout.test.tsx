import { render, screen } from '@solidjs/testing-library';
import type { Location } from '@solidjs/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { I18nProvider } from '../i18n/i18n.tsx';
import { ThemeProvider } from '../shared/theme/theme-context.tsx';
import { AppLayout } from './layout.tsx';

const authState = vi.hoisted(() => ({
  authenticated: false,
  enabled: true,
  login: vi.fn(),
  logout: vi.fn()
}));

vi.mock('../features/auth/auth-context.tsx', () => ({
  useAuth: () => ({
    isAuthenticated: () => authState.authenticated,
    isEnabled: authState.enabled,
    login: authState.login,
    logout: authState.logout
  })
}));

const location: Location = {
  pathname: '/',
  search: '',
  hash: '',
  query: {},
  state: null,
  key: 'test'
};

const renderLayout = (variant: 'app' | 'share' = 'app') =>
  render(() => (
    <I18nProvider>
      <ThemeProvider>
        <AppLayout params={{}} location={location} data={undefined} variant={variant}>
          <p>page content</p>
        </AppLayout>
      </ThemeProvider>
    </I18nProvider>
  ));

describe('<AppLayout />', () => {
  beforeEach(() => {
    authState.authenticated = false;
    authState.enabled = true;
    authState.login.mockClear();
    authState.logout.mockClear();
    localStorage.clear();
    document.documentElement.classList.remove('dark');
  });

  it('shows the sign-in action for anonymous visitors when auth is enabled', () => {
    renderLayout();

    expect(screen.getByText('ShareIt').closest('a')).not.toBeNull();
    expect(screen.queryByText('upload')).not.toBeInTheDocument();
    expect(screen.getByText('page content')).toBeInTheDocument();

    screen.getByRole('button', { name: 'Sign in' }).click();

    expect(authState.login).toHaveBeenCalledTimes(1);
  });

  it('shows the app navigation and sign-out action for signed-in users', () => {
    authState.authenticated = true;
    renderLayout();

    expect(screen.getByText('upload').closest('a')).not.toBeNull();
    expect(screen.getByRole('link', { name: 'links' })).toHaveAttribute('href', '/links');
    expect(screen.queryByRole('button', { name: 'Sign in' })).not.toBeInTheDocument();

    screen.getByRole('button', { name: 'log out' }).click();

    expect(authState.logout).toHaveBeenCalledTimes(1);
  });

  it('hides auth controls when OIDC is not configured', () => {
    authState.enabled = false;
    renderLayout();

    expect(screen.queryByRole('button', { name: 'Sign in' })).not.toBeInTheDocument();
  });

  it('keeps only the brand and theme toggle on share links', () => {
    authState.authenticated = true;
    renderLayout('share');

    expect(screen.getByText('ShareIt').closest('a')).not.toBeNull();
    expect(screen.getByRole('button', { name: 'Toggle theme' })).toBeInTheDocument();
    expect(screen.queryByText('upload')).not.toBeInTheDocument();
    expect(screen.queryByText('links')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'log out' })).not.toBeInTheDocument();
  });
});
