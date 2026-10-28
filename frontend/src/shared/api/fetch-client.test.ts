import { beforeEach, describe, expect, it, vi } from 'vitest';

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

import { absoluteApiUrl, getAuthHeaders } from './fetch-client.ts';

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
});
