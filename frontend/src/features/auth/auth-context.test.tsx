import { render } from '@solidjs/testing-library';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider, useAuth } from './auth-context.tsx';

const oidc = vi.hoisted(() => {
  type FakeUser = { access_token: string; profile: { sub: string } };

  const manager = {
    getUser: vi.fn<() => Promise<FakeUser | null>>(async () => null),
    signinRedirect: vi.fn(async () => undefined),
    signoutRedirect: vi.fn(async () => undefined),
    signinRedirectCallback: vi.fn(async () => ({ access_token: 'callback-token' })),
    events: {
      addUserLoaded: vi.fn(),
      removeUserLoaded: vi.fn(),
      addUserUnloaded: vi.fn(),
      removeUserUnloaded: vi.fn()
    }
  };

  return {
    manager,
    state: { enabled: false }
  };
});

vi.mock('../../shared/auth/oidc.ts', () => ({
  getOidcSettings: () => (oidc.state.enabled ? { authority: 'https://auth.example', client_id: 'shareit' } : null),
  getUserManager: () => (oidc.state.enabled ? oidc.manager : null)
}));

const fakeUser = {
  access_token: 'access-token',
  profile: { sub: 'user-1' }
};

type Auth = ReturnType<typeof useAuth>;

const renderAuth = () => {
  let auth: Auth | undefined;
  const Harness = () => {
    auth = useAuth();
    return <div>{auth.isAuthenticated() ? 'signed in' : 'signed out'}</div>;
  };
  const result = render(() => (
    <AuthProvider>
      <Harness />
    </AuthProvider>
  ));

  if (!auth) {
    throw new Error('auth context was not created');
  }

  return { auth, ...result };
};

const waitForLoaded = async (auth: Auth) => {
  await vi.waitFor(() => expect(auth.isLoading()).toBe(false));
};

describe('AuthProvider', () => {
  beforeEach(() => {
    oidc.state.enabled = false;
    oidc.manager.getUser.mockReset();
    oidc.manager.getUser.mockResolvedValue(null);
    oidc.manager.signinRedirect.mockClear();
    oidc.manager.signoutRedirect.mockClear();
    oidc.manager.signinRedirectCallback.mockClear();
    oidc.manager.events.addUserLoaded.mockClear();
    oidc.manager.events.removeUserLoaded.mockClear();
    oidc.manager.events.addUserUnloaded.mockClear();
    oidc.manager.events.removeUserUnloaded.mockClear();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it('is disabled and exposes no-op auth actions when OIDC is not configured', async () => {
    const { auth } = renderAuth();

    await waitForLoaded(auth);

    expect(auth.isEnabled).toBe(false);
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.user()).toBeNull();
    expect(auth.login()).toBeUndefined();
    expect(auth.logout()).toBeUndefined();
    await auth.handleCallback();
    expect(oidc.manager.signinRedirect).not.toHaveBeenCalled();
    expect(oidc.manager.signoutRedirect).not.toHaveBeenCalled();
    expect(oidc.manager.signinRedirectCallback).not.toHaveBeenCalled();
  });

  it('loads the stored user and exposes the authenticated state', async () => {
    oidc.state.enabled = true;
    oidc.manager.getUser.mockResolvedValue(fakeUser);

    const result = renderAuth();
    const { auth } = result;

    expect(await result.findByText('signed in')).toBeInTheDocument();
    await waitForLoaded(auth);
    expect(auth.isEnabled).toBe(true);
    expect(auth.user()).toBe(fakeUser);
    expect(auth.isAuthenticated()).toBe(true);
  });

  it('delegates login and logout to the user manager', async () => {
    oidc.state.enabled = true;

    const { auth } = renderAuth();
    await waitForLoaded(auth);

    await auth.login();
    await auth.logout();

    expect(oidc.manager.signinRedirect).toHaveBeenCalledTimes(1);
    expect(oidc.manager.signoutRedirect).toHaveBeenCalledTimes(1);
  });

  it('completes the callback and reloads the user', async () => {
    oidc.state.enabled = true;
    oidc.manager.getUser.mockResolvedValueOnce(null).mockResolvedValueOnce(fakeUser);

    const { auth } = renderAuth();
    await waitForLoaded(auth);

    await auth.handleCallback();

    expect(oidc.manager.signinRedirectCallback).toHaveBeenCalledTimes(1);
    expect(auth.user()).toBe(fakeUser);
    expect(auth.isAuthenticated()).toBe(true);
  });

  it('reacts to user loaded and unloaded events and unsubscribes on cleanup', async () => {
    oidc.state.enabled = true;

    const { auth, unmount } = renderAuth();
    await waitForLoaded(auth);

    const loadedHandler = oidc.manager.events.addUserLoaded.mock.calls[0][0];
    const unloadedHandler = oidc.manager.events.addUserUnloaded.mock.calls[0][0];

    loadedHandler(fakeUser);
    await vi.waitFor(() => expect(auth.isAuthenticated()).toBe(true));

    unloadedHandler();
    await vi.waitFor(() => expect(auth.isAuthenticated()).toBe(false));

    unmount();

    expect(oidc.manager.events.removeUserLoaded).toHaveBeenCalledWith(loadedHandler);
    expect(oidc.manager.events.removeUserUnloaded).toHaveBeenCalledWith(unloadedHandler);
  });
});
