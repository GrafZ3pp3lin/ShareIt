import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest';

const { getAuthTokenMock, getDpopAuthHeadersMock } = vi.hoisted(() => ({
  getAuthTokenMock: vi.fn<() => Promise<string | undefined>>(),
  getDpopAuthHeadersMock: vi.fn<() => Promise<Record<string, string>>>()
}));

vi.mock('../config/runtime-config.ts', () => ({
  getRuntimeConfig: () => ({ apiBaseUrl: 'https://api.example.test' })
}));

vi.mock('../auth/oidc.ts', () => ({
  getAuthToken: getAuthTokenMock,
  getDpopAuthHeaders: getDpopAuthHeadersMock
}));

import { absoluteApiUrl, customFetch, getAuthHeaders } from './fetch-client.ts';

const originalFetch = globalThis.fetch;

afterEach(() => {
  globalThis.fetch = originalFetch;
});

describe('absoluteApiUrl', () => {
  it('prepends the API base URL', () => {
    expect(absoluteApiUrl('/api/v1/content/package/1')).toBe('https://api.example.test/api/v1/content/package/1');
  });

  it('strips query and fragment for the DPoP htu binding', () => {
    expect(absoluteApiUrl('/api/v1/content/package/1?version=2#frag')).toBe(
      'https://api.example.test/api/v1/content/package/1'
    );
  });
});

describe('getAuthHeaders', () => {
  beforeEach(() => {
    getAuthTokenMock.mockReset();
    getDpopAuthHeadersMock.mockReset();
  });

  it('prefers DPoP headers when a proof is available', async () => {
    const dpopHeaders = { Authorization: 'DPoP token', DPoP: 'proof-jwt' };
    getDpopAuthHeadersMock.mockResolvedValue(dpopHeaders);

    const headers = await getAuthHeaders('https://api.example.test/api/v1/content', 'GET');

    expect(headers).toEqual(dpopHeaders);
    expect(getAuthTokenMock).not.toHaveBeenCalled();
    expect(getDpopAuthHeadersMock).toHaveBeenCalledWith('https://api.example.test/api/v1/content', 'GET');
  });

  it('falls back to the bearer token when no DPoP proof is generated', async () => {
    getDpopAuthHeadersMock.mockResolvedValue({});
    getAuthTokenMock.mockResolvedValue('access-token');

    const headers = await getAuthHeaders('https://api.example.test/api/v1/content', 'POST');

    expect(headers).toEqual({ Authorization: 'Bearer access-token' });
  });

  it('does not attempt DPoP without a target URL', async () => {
    getAuthTokenMock.mockResolvedValue('access-token');

    const headers = await getAuthHeaders();

    expect(headers).toEqual({ Authorization: 'Bearer access-token' });
    expect(getDpopAuthHeadersMock).not.toHaveBeenCalled();
  });

  it('returns no identity headers when there is no token', async () => {
    getDpopAuthHeadersMock.mockResolvedValue({});
    getAuthTokenMock.mockResolvedValue(undefined);

    const headers = await getAuthHeaders('https://api.example.test/api/v1/content', 'GET');

    expect(headers).toEqual({});
  });
});

describe('customFetch', () => {
  beforeEach(() => {
    getAuthTokenMock.mockReset();
    getDpopAuthHeadersMock.mockReset();
    getDpopAuthHeadersMock.mockResolvedValue({});
    getAuthTokenMock.mockResolvedValue(undefined);
  });

  it('prefixes the runtime API base URL and sends no identity headers for a guest', async () => {
    const fetchMock = vi.fn<typeof fetch>(
      async () =>
        new Response(JSON.stringify({ ok: true }), {
          status: 201,
          headers: { 'content-type': 'application/json', 'x-request-id': 'request-1' }
        })
    );
    globalThis.fetch = fetchMock;

    const result = await customFetch<{ data: unknown; status: number; headers: Headers }>('/api/v1/content/package', {
      method: 'POST'
    });

    expect(result.data).toEqual({ ok: true });
    expect(result.status).toBe(201);
    expect(result.headers.get('x-request-id')).toBe('request-1');
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('https://api.example.test/api/v1/content/package');
    expect(init?.method).toBe('POST');
    expect(new Headers(init?.headers).get('Authorization')).toBeNull();
  });

  it('keeps caller-supplied identity headers', async () => {
    const fetchMock = vi.fn<typeof fetch>(async () => new Response(null, { status: 204 }));
    globalThis.fetch = fetchMock;

    await customFetch('/api/v1/content/package', {
      headers: { Authorization: 'Custom token' }
    });

    expect(new Headers(fetchMock.mock.calls[0][1]?.headers).get('Authorization')).toBe('Custom token');
    expect(getAuthTokenMock).not.toHaveBeenCalled();
  });

  it('parses text responses', async () => {
    globalThis.fetch = vi.fn<typeof fetch>(
      async () => new Response('plain body', { headers: { 'content-type': 'text/plain' } })
    );

    const result = await customFetch<{ data: unknown }>('/api/v1/content/text');

    expect(result.data).toBe('plain body');
  });

  it('parses binary responses as a blob', async () => {
    globalThis.fetch = vi.fn<typeof fetch>(
      async () =>
        new Response(new Blob(['bytes']), {
          headers: { 'content-type': 'application/octet-stream' }
        })
    );

    const result = await customFetch<{ data: unknown }>('/api/v1/content/file');

    expect(result.data).toBeInstanceOf(Blob);
  });

  it('parses problem+json error responses', async () => {
    globalThis.fetch = vi.fn<typeof fetch>(
      async () =>
        new Response(JSON.stringify({ title: 'Invalid access session' }), {
          status: 401,
          headers: { 'content-type': 'application/problem+json' }
        })
    );

    const result = await customFetch<{ data: unknown; status: number }>('/api/v1/content/package/1/content/2/download');

    expect(result.data).toEqual({ title: 'Invalid access session' });
    expect(result.status).toBe(401);
  });

  it('leaves the body undefined for malformed JSON', async () => {
    globalThis.fetch = vi.fn<typeof fetch>(
      async () => new Response('not json', { headers: { 'content-type': 'application/json' } })
    );

    const result = await customFetch<{ data: unknown }>('/api/v1/content/package');

    expect(result.data).toBeUndefined();
  });
});
