import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const ENV_KEYS = [
  'VITE_API_BASE_URL',
  'VITE_OIDC_AUTHORITY',
  'VITE_OIDC_CLIENT_ID',
  'VITE_OIDC_REDIRECT_URI',
  'VITE_OIDC_POST_LOGOUT_REDIRECT_URI',
  'VITE_OIDC_SCOPE',
  'VITE_OIDC_DPOP'
] as const;

const loadConfig = async () => {
  vi.resetModules();
  return (await import('./runtime-config.ts')).getRuntimeConfig();
};

const clearViteEnv = () => {
  for (const key of ENV_KEYS) {
    vi.stubEnv(key, undefined);
  }
};

describe('getRuntimeConfig', () => {
  beforeEach(() => {
    vi.resetModules();
    vi.unstubAllGlobals();
    vi.unstubAllEnvs();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.unstubAllEnvs();
  });

  it('reads the Vite values when no runtime env is present', async () => {
    clearViteEnv();
    vi.stubEnv('VITE_API_BASE_URL', 'https://vite-api.example');
    vi.stubEnv('VITE_OIDC_AUTHORITY', 'https://vite-auth.example/realms/test');

    const config = await loadConfig();

    expect(config.apiBaseUrl).toBe('https://vite-api.example');
    expect(config.authAuthority).toBe('https://vite-auth.example/realms/test');
  });

  it('prefers runtime values, trims them and allows Vite fallback per field', async () => {
    clearViteEnv();
    vi.stubEnv('VITE_API_BASE_URL', 'https://vite-api.example');
    vi.stubEnv('VITE_OIDC_AUTHORITY', 'https://vite-auth.example/realms/test');
    vi.stubGlobal('window', {
      env: {
        ApiBaseUrl: '  https://runtime-api.example  ',
        AuthAuthority: '   ',
        AuthClientId: 'runtime-client',
        AuthRedirectUri: 'https://app.example/callback',
        AuthPostLogoutRedirectUri: 'https://app.example',
        AuthScope: 'openid profile'
      }
    });

    const config = await loadConfig();

    expect(config.apiBaseUrl).toBe('https://runtime-api.example');
    expect(config.authAuthority).toBe('https://vite-auth.example/realms/test');
    expect(config.authClientId).toBe('runtime-client');
    expect(config.authRedirectUri).toBe('https://app.example/callback');
    expect(config.authPostLogoutRedirectUri).toBe('https://app.example');
    expect(config.authScope).toBe('openid profile');
  });

  it('leaves missing values undefined and enables DPoP by default', async () => {
    clearViteEnv();

    const config = await loadConfig();

    expect(config.apiBaseUrl).toBeUndefined();
    expect(config.authAuthority).toBeUndefined();
    expect(config.authClientId).toBeUndefined();
    expect(config.authRedirectUri).toBeUndefined();
    expect(config.authPostLogoutRedirectUri).toBeUndefined();
    expect(config.authScope).toBeUndefined();
    expect(config.authDpop).toBe(true);
  });

  it('parses the DPoP flag from runtime values before Vite values', async () => {
    clearViteEnv();
    vi.stubEnv('VITE_OIDC_DPOP', 'false');
    vi.stubGlobal('window', { env: { AuthDpop: 'true' } });

    expect((await loadConfig()).authDpop).toBe(true);

    vi.stubGlobal('window', { env: { AuthDpop: false } });
    expect((await loadConfig()).authDpop).toBe(false);
  });

  it('treats only the literal "true" string as enabled when falling back to Vite', async () => {
    clearViteEnv();
    vi.stubEnv('VITE_OIDC_DPOP', 'yes');

    expect((await loadConfig()).authDpop).toBe(false);
  });
});
