import { UserManager, type UserManagerSettings } from 'oidc-client-ts';

let manager: UserManager | null | undefined;

export const getOidcSettings = (): UserManagerSettings | null => {
  const authority = import.meta.env.VITE_OIDC_AUTHORITY;
  const clientId = import.meta.env.VITE_OIDC_CLIENT_ID;
  const redirectUri = import.meta.env.VITE_OIDC_REDIRECT_URI;
  if (!authority || !clientId || !redirectUri) {
    return null;
  }

  return {
    authority,
    client_id: clientId,
    redirect_uri: redirectUri,
    response_type: 'code',
    scope: 'openid profile email',
    post_logout_redirect_uri: import.meta.env.VITE_OIDC_POST_LOGOUT_REDIRECT_URI ?? window.location.origin,
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
