import { client } from '../../client/client.gen';
import { getAuthToken } from '../auth/oidc';
import { getGuestId } from './guest';

let isConfigured = false;

export const configureApiClient = () => {
  if (isConfigured) {
    return;
  }

  isConfigured = true;
  const baseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5155';
  client.setConfig({
    baseUrl,
    responseStyle: 'fields',
    throwOnError: false,
  });

  client.interceptors.request.use(async (request) => {
    const headers = new Headers(request.headers);
    if (headers.has('Authorization') || headers.has('X-Guest-ID')) {
      return request;
    }

    const authToken = await getAuthToken();
    if (authToken) {
      headers.set('Authorization', `Bearer ${authToken}`);
    } else {
      const guestId = getGuestId();
      if (guestId) {
        headers.set('X-Guest-ID', guestId);
      }
    }

    return new Request(request, { headers });
  });
};
