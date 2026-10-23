import { getRuntimeConfig } from '../config/runtime-config.ts';
import { getAuthToken } from '../auth/oidc.ts';
import { getGuestId } from './guest.ts';

/**
 * Orval mutator (fetch client). Adds the runtime-configured base URL and the
 * identity headers (OIDC bearer token or persisted guest ID), then returns the
 * generated `{ data, status, headers }` response shape.
 */
export const customFetch = async <T>(url: string, options: RequestInit = {}): Promise<T> => {
  const headers = new Headers(options.headers);
  if (!headers.has('Authorization') && !headers.has('X-Guest-ID')) {
    for (const [name, value] of Object.entries(await getAuthHeaders())) {
      headers.set(name, value);
    }
  }

  const response = await fetch(`${getRuntimeConfig().apiBaseUrl}${url}`, {
    ...options,
    headers
  });
  const body = await parseBody(response);

  // The generic is supplied by each generated call site; the literal response shape
  // satisfies every generated `{ data, status, headers }` response union.
  return { data: body, status: response.status, headers: response.headers } as T;
};

const parseBody = async (response: Response): Promise<unknown> => {
  const contentType = response.headers.get('content-type') ?? '';
  if (contentType.includes('application/json')) {
    try {
      return await response.json();
    } catch {
      return undefined;
    }
  }
  if (contentType.startsWith('text/')) {
    return await response.text();
  }
  return await response.blob();
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
