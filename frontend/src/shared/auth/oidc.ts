import { UserManager, type UserManagerSettings } from 'oidc-client-ts';
import { getRuntimeConfig } from '../config/runtime-config';

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
