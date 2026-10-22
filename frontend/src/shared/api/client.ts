import { client } from '../../client/client.gen.ts';
import { getRuntimeConfig } from '../config/runtime-config.ts';
import { getAuthToken } from '../auth/oidc.ts';
import { getGuestId } from './guest.ts';

let isConfigured = false;

export const configureApiClient = () => {
  if (isConfigured) {
    return;
  }

  isConfigured = true;
  const baseUrl = getRuntimeConfig().apiBaseUrl;
  client.setConfig({
    baseUrl,
    responseStyle: 'fields',
    throwOnError: false
  });

  client.interceptors.request.use(async (request) => {
    const headers = new Headers(request.headers);
    if (headers.has('Authorization') || headers.has('X-Guest-ID')) {
      return request;
    }

    for (const [name, value] of Object.entries(await getAuthHeaders())) {
      headers.set(name, value);
    }

    return new Request(request, { headers });
  });
};

/**
 * Returns the identity headers to send with API requests: an OIDC bearer token
 * when authenticated, otherwise the persisted guest ID.
 */
export const getAuthHeaders = async (): Promise<Record<string, string>> => {
  const headers: Record<string, string> = {};
  const authToken = await getAuthToken();
  if (authToken) {
    headers['Authorization'] = `Bearer ${authToken}`;
    return headers;
  }

  const guestId = getGuestId();
  if (guestId) {
    headers['X-Guest-ID'] = guestId;
  }
  return headers;
};
