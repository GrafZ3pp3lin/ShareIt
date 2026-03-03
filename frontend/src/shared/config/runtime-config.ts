export type RuntimeEnv = {
  ApiBaseUrl?: string;
  AuthAuthority?: string;
  AuthClientId?: string;
  AuthRedirectUri?: string;
  AuthPostLogoutRedirectUri?: string;
  AuthScope?: string;
};

export type RuntimeConfig = {
  apiBaseUrl?: string;
  authAuthority?: string;
  authClientId?: string;
  authRedirectUri?: string;
  authPostLogoutRedirectUri?: string;
  authScope?: string;
};

type ViteEnvKey =
  | 'VITE_API_BASE_URL'
  | 'VITE_OIDC_AUTHORITY'
  | 'VITE_OIDC_CLIENT_ID'
  | 'VITE_OIDC_REDIRECT_URI'
  | 'VITE_OIDC_POST_LOGOUT_REDIRECT_URI'
  | 'VITE_OIDC_SCOPE';

const normalize = (value: string | undefined): string | undefined => {
  if (!value) {
    return undefined;
  }

  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : undefined;
};

const readRuntimeEnv = (): RuntimeEnv => {
  if (typeof window === 'undefined' || !window.env) {
    return {};
  }

  return window.env;
};

const readViteEnv = (key: ViteEnvKey): string | undefined => {
  return normalize(import.meta.env[key]);
};

const readConfigValue = (runtimeValue: string | undefined, viteKey: ViteEnvKey): string | undefined => {
  return normalize(runtimeValue) ?? readViteEnv(viteKey);
};

const createRuntimeConfig = (): RuntimeConfig => {
  const runtimeEnv = readRuntimeEnv();

  return {
    apiBaseUrl: readConfigValue(runtimeEnv.ApiBaseUrl, 'VITE_API_BASE_URL'),
    authAuthority: readConfigValue(runtimeEnv.AuthAuthority, 'VITE_OIDC_AUTHORITY'),
    authClientId: readConfigValue(runtimeEnv.AuthClientId, 'VITE_OIDC_CLIENT_ID'),
    authRedirectUri: readConfigValue(runtimeEnv.AuthRedirectUri, 'VITE_OIDC_REDIRECT_URI'),
    authPostLogoutRedirectUri: readConfigValue(
      runtimeEnv.AuthPostLogoutRedirectUri,
      'VITE_OIDC_POST_LOGOUT_REDIRECT_URI'
    ),
    authScope: readConfigValue(runtimeEnv.AuthScope, 'VITE_OIDC_SCOPE'),
  };
};

const runtimeConfig = createRuntimeConfig();

export const getRuntimeConfig = () => {
  return runtimeConfig;
};
