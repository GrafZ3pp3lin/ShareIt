import { render, screen } from '@solidjs/testing-library';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { I18nProvider } from '../../../i18n/i18n.tsx';
import { AuthCallbackPage } from './auth-callback-page.tsx';

const authState = vi.hoisted(() => ({
  enabled: true,
  handleCallback: vi.fn(async () => undefined)
}));

const navigateMock = vi.hoisted(() => vi.fn());

vi.mock('../../auth/auth-context.tsx', () => ({
  useAuth: () => ({
    isEnabled: authState.enabled,
    handleCallback: authState.handleCallback
  })
}));

vi.mock('@solidjs/router', () => ({
  useNavigate: () => navigateMock
}));

const renderPage = () =>
  render(() => (
    <I18nProvider>
      <AuthCallbackPage />
    </I18nProvider>
  ));

describe('<AuthCallbackPage />', () => {
  beforeEach(() => {
    authState.enabled = true;
    authState.handleCallback.mockClear();
    navigateMock.mockClear();
  });

  it('finishes the sign-in and navigates home when OIDC is enabled', async () => {
    renderPage();

    expect(screen.getByText('Finishing sign-in...')).toBeInTheDocument();

    await vi.waitFor(() => expect(authState.handleCallback).toHaveBeenCalledTimes(1));
    await vi.waitFor(() => expect(navigateMock).toHaveBeenCalledWith('/'));
  });

  it('shows the not-configured message and does nothing when OIDC is disabled', async () => {
    authState.enabled = false;

    renderPage();

    expect(screen.getByText('OIDC is not configured.')).toBeInTheDocument();
    await Promise.resolve();
    expect(authState.handleCallback).not.toHaveBeenCalled();
    expect(navigateMock).not.toHaveBeenCalled();
  });
});
