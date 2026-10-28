import { IndexedDbDPoPStore, UserManager, type UserManagerSettings } from 'oidc-client-ts';
import { getRuntimeConfig } from '../config/runtime-config.ts';

let manager: UserManager | null | undefined;

export const getOidcSettings = (): UserManagerSettings | null => {
  const config = getRuntimeConfig();
  if (!config.authAuthority || !config.authClientId) {
    return null;
  }

  return {
    authority: config.authAuthority,
    client_id: config.authClientId,
    scope: config.authScope,
    redirect_uri: config.authRedirectUri ?? `${window.location.origin}/auth/callback`,
    post_logout_redirect_uri: config.authPostLogoutRedirectUri ?? window.location.origin,
    response_type: 'code',
    // DPoP (RFC 9449): bind tokens to a non-extractable client key pair held in
    // IndexedDB. Requires the Keycloak client to require DPoP-bound tokens
    // (Keycloak >= 26.4) so the issued tokens carry `cnf.jkt`, which the API
    // then enforces via Auth:Dpop:Required.
    dpop: config.authDpop
      ? {
          bind_authorization_code: true,
          store: new IndexedDbDPoPStore()
        }
      : undefined
  };
};

export const getUserManager = () => {
  if (manager !== undefined) {
    return manager;
  }

  const settings = getOidcSettings();
  manager = settings ? new UserManager(settings) : null;
  return manager;
};

export const getAuthToken = async () => {
  const currentManager = getUserManager();
  if (!currentManager) {
    return undefined;
  }

  const user = await currentManager.getUser();
  return user?.access_token;
};

/**
 * Builds DPoP identity headers (`Authorization: DPoP` + one-time `DPoP` proof)
 * for an absolute request URL. Returns an empty object when DPoP is disabled,
 * no manager/user exists, or the proof could not be generated — callers fall
 * back to bearer headers in that case. The proof binds the exact URL (without
 * query/fragment, per RFC 9449) and HTTP method.
 */
export const getDpopAuthHeaders = async (url: string, httpMethod: string): Promise<Record<string, string>> => {
  const currentManager = getUserManager();
  if (!currentManager?.settings.dpop) {
    return {};
  }

  const user = await currentManager.getUser();
  if (!user?.access_token) {
    return {};
  }

  // Proof generation touches IndexedDB and WebCrypto; treat any failure as
  // "no proof available" so callers fall back to plain bearer headers.
  try {
    const proof = await currentManager.dpopProof(url, user, httpMethod);
    if (!proof) {
      return {};
    }

    return { Authorization: `DPoP ${user.access_token}`, DPoP: proof };
  } catch {
    return {};
  }
};
