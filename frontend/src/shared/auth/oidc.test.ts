import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

type Config = {
  authAuthority?: string;
  authClientId?: string;
  authRedirectUri?: string;
  authPostLogoutRedirectUri?: string;
  authScope?: string;
  authDpop: boolean;
};

const configState = vi.hoisted<{ current: Config }>(() => ({
  current: { authDpop: true }
}));

const oidcMock = vi.hoisted(() => {
  type FakeUser = { access_token: string };

  const instances: FakeUserManager[] = [];

  class FakeUserManager {
    settings: Record<string, unknown>;
    events = {
      addUserLoaded: vi.fn(),
      removeUserLoaded: vi.fn(),
      addUserUnloaded: vi.fn(),
      removeUserUnloaded: vi.fn()
    };
    getUser = vi.fn<() => Promise<FakeUser | null>>(async () => null);
    signinRedirect = vi.fn(async () => undefined);
    signoutRedirect = vi.fn(async () => undefined);
    signinRedirectCallback = vi.fn(async () => ({ access_token: 'callback-token' }));
    dpopProof = vi.fn<() => Promise<string | null>>(async () => null);

    constructor(settings: Record<string, unknown>) {
      this.settings = settings;
      instances.push(this);
    }
  }

  class FakeIndexedDbDPoPStore {
    readonly kind = 'indexeddb';
  }

  return { FakeUserManager, FakeIndexedDbDPoPStore, instances };
});

vi.mock('oidc-client-ts', () => ({
  UserManager: oidcMock.FakeUserManager,
  IndexedDbDPoPStore: oidcMock.FakeIndexedDbDPoPStore
}));

vi.mock('../config/runtime-config.ts', () => ({
  getRuntimeConfig: () => configState.current
}));

const baseConfig: Config = {
  authAuthority: 'https://auth.example/realms/shareit',
  authClientId: 'shareit-spa',
  authScope: 'openid profile',
  authDpop: true
};

const loadOidc = async () => {
  vi.resetModules();
  return import('./oidc.ts');
};

describe('getOidcSettings', () => {
  beforeEach(() => {
    vi.resetModules();
    configState.current = { ...baseConfig };
    oidcMock.instances.length = 0;
    vi.stubGlobal('window', { location: { origin: 'https://app.example' } });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('returns null when the authority or client id is missing', async () => {
    configState.current = { ...baseConfig, authAuthority: undefined };
    expect((await loadOidc()).getOidcSettings()).toBeNull();

    configState.current = { ...baseConfig, authClientId: '' };
    expect((await loadOidc()).getOidcSettings()).toBeNull();
  });

  it('builds the authorization-code settings with default redirect URIs', async () => {
    const { getOidcSettings } = await loadOidc();

    const settings = getOidcSettings();

    expect(settings).toMatchObject({
      authority: baseConfig.authAuthority,
      client_id: baseConfig.authClientId,
      scope: baseConfig.authScope,
      redirect_uri: 'https://app.example/auth/callback',
      post_logout_redirect_uri: 'https://app.example',
      response_type: 'code'
    });
    expect(settings?.dpop).toMatchObject({ bind_authorization_code: true });
    expect(settings?.dpop?.store).toBeInstanceOf(oidcMock.FakeIndexedDbDPoPStore);
  });

  it('uses configured redirect URIs and can disable DPoP', async () => {
    configState.current = {
      ...baseConfig,
      authRedirectUri: 'https://app.example/login/callback',
      authPostLogoutRedirectUri: 'https://app.example/goodbye',
      authDpop: false
    };

    const settings = (await loadOidc()).getOidcSettings();

    expect(settings?.redirect_uri).toBe('https://app.example/login/callback');
    expect(settings?.post_logout_redirect_uri).toBe('https://app.example/goodbye');
    expect(settings?.dpop).toBeUndefined();
  });
});

describe('getUserManager', () => {
  beforeEach(() => {
    vi.resetModules();
    configState.current = { ...baseConfig };
    oidcMock.instances.length = 0;
    vi.stubGlobal('window', { location: { origin: 'https://app.example' } });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('returns null and stays null when auth is not configured', async () => {
    configState.current = { ...baseConfig, authClientId: undefined };
    const { getUserManager } = await loadOidc();

    expect(getUserManager()).toBeNull();
    expect(getUserManager()).toBeNull();
    expect(oidcMock.instances).toHaveLength(0);
  });

  it('memoizes the manager singleton', async () => {
    const { getUserManager } = await loadOidc();

    const first = getUserManager();
    const second = getUserManager();

    expect(first).toBe(second);
    expect(oidcMock.instances).toHaveLength(1);
    expect(oidcMock.instances[0]).toBe(first);
  });
});

describe('getAuthToken', () => {
  beforeEach(() => {
    vi.resetModules();
    configState.current = { ...baseConfig };
    oidcMock.instances.length = 0;
    vi.stubGlobal('window', { location: { origin: 'https://app.example' } });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('returns undefined when there is no manager', async () => {
    configState.current = { ...baseConfig, authAuthority: undefined };

    expect(await (await loadOidc()).getAuthToken()).toBeUndefined();
  });

  it('returns the stored user access token', async () => {
    const { getAuthToken, getUserManager } = await loadOidc();
    getUserManager();
    oidcMock.instances[0].getUser.mockResolvedValue({ access_token: 'access-token' });

    expect(await getAuthToken()).toBe('access-token');
  });
});

describe('getDpopAuthHeaders', () => {
  beforeEach(() => {
    vi.resetModules();
    configState.current = { ...baseConfig };
    oidcMock.instances.length = 0;
    vi.stubGlobal('window', { location: { origin: 'https://app.example' } });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('returns no headers when DPoP is disabled or there is no user', async () => {
    configState.current = { ...baseConfig, authDpop: false };
    const oidc = await loadOidc();
    oidc.getUserManager();

    expect(await oidc.getDpopAuthHeaders('https://api.example/items/1', 'GET')).toEqual({});
    expect(oidcMock.instances[0].dpopProof).not.toHaveBeenCalled();
  });

  it('returns no headers without an access token', async () => {
    const oidc = await loadOidc();
    oidc.getUserManager();
    oidcMock.instances[0].getUser.mockResolvedValue(null);

    expect(await oidc.getDpopAuthHeaders('https://api.example/items/1', 'GET')).toEqual({});
  });

  it('binds the proof to the URL and method', async () => {
    const oidc = await loadOidc();
    oidc.getUserManager();
    const manager = oidcMock.instances[0];
    manager.getUser.mockResolvedValue({ access_token: 'access-token' });
    manager.dpopProof.mockResolvedValue('proof-jwt');

    const headers = await oidc.getDpopAuthHeaders('https://api.example/items/1', 'POST');

    expect(headers).toEqual({ Authorization: 'DPoP access-token', DPoP: 'proof-jwt' });
    expect(manager.dpopProof).toHaveBeenCalledWith('https://api.example/items/1', expect.anything(), 'POST');
  });

  it('falls back to no headers when proof generation fails or returns nothing', async () => {
    const oidc = await loadOidc();
    oidc.getUserManager();
    const manager = oidcMock.instances[0];
    manager.getUser.mockResolvedValue({ access_token: 'access-token' });
    manager.dpopProof.mockResolvedValueOnce(null);
    manager.dpopProof.mockRejectedValueOnce(new Error('IndexedDB unavailable'));

    expect(await oidc.getDpopAuthHeaders('https://api.example/items/1', 'GET')).toEqual({});
    expect(await oidc.getDpopAuthHeaders('https://api.example/items/1', 'GET')).toEqual({});
  });
});
