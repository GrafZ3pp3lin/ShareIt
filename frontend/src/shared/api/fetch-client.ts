import { getRuntimeConfig } from '../config/runtime-config.ts';
import { getAuthToken, getDpopAuthHeaders } from '../auth/oidc.ts';

/**
 * Orval mutator (fetch client). Adds the runtime-configured base URL and the
 * caller's OIDC DPoP/bearer headers, then returns the generated
 * `{ data, status, headers }` response shape.
 */
export const customFetch = async <T>(url: string, options: RequestInit = {}): Promise<T> => {
  const headers = new Headers(options.headers);
  if (!headers.has('Authorization')) {
    for (const [name, value] of Object.entries(await getAuthHeaders(absoluteApiUrl(url), options.method ?? 'GET'))) {
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
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  return { data: body, status: response.status, headers: response.headers } as T;
};

/**
 * Parses an RFC 7807 problem response without consuming non-JSON error bodies.
 * Used by the hand-written raw-response download path to stay in step with the
 * generated client.
 */
export const parseProblem = async (response: Response): Promise<unknown> => {
  const contentType = response.headers.get('content-type') ?? '';
  if (!contentType.includes('json')) {
    return undefined;
  }

  try {
    return await response.json();
  } catch {
    return undefined;
  }
};

const parseBody = async (response: Response): Promise<unknown> => {
  const contentType = response.headers.get('content-type') ?? '';
  if (contentType.includes('application/json') || contentType.includes('+json')) {
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
 * Builds the absolute API URL for a generated-client path, stripped of query
 * and fragment. DPoP proofs bind `htu` to exactly this canonical form
 * (RFC 9449: scheme, authority and path only).
 */
export const absoluteApiUrl = (url: string): string => {
  const path = url.split(/[?#]/, 1)[0];
  return `${getRuntimeConfig().apiBaseUrl}${path}`;
};

/**
 * Returns the identity headers to send with API requests. When a target URL is
 * given, DPoP headers are preferred for authenticated requests (falling back to
 * a plain bearer token when DPoP is disabled or no proof could be generated);
 * otherwise only the bearer token is attached. Unauthenticated callers send no
 * identity headers: guests have no id of their own.
 */
export const getAuthHeaders = async (url?: string, method: string = 'GET'): Promise<Record<string, string>> => {
  if (url) {
    const dpopHeaders = await getDpopAuthHeaders(url, method);
    if (Object.keys(dpopHeaders).length > 0) {
      return dpopHeaders;
    }
  }

  const headers: Record<string, string> = {};
  const authToken = await getAuthToken();
  if (authToken) {
    headers['Authorization'] = `Bearer ${authToken}`;
  }
  return headers;
};
