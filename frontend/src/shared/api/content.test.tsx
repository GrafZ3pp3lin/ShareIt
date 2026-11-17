import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { FakeXhr } from '../../tests/fake-xhr.ts';
import {
  createPackage,
  deletePackage,
  downloadContent,
  getPackageDetails,
  getUploadLimits,
  listPackages,
  uploadItem
} from './content.ts';

const originalXhr = globalThis.XMLHttpRequest;
const originalFetch = globalThis.fetch;

const jsonResponse = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });

const problemResponse = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/problem+json' } });

const stubFetch = (handler: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>) => {
  const fetchMock = vi.fn(handler);
  globalThis.fetch = fetchMock;
  return fetchMock;
};

const makeFile = (name = 'notes.txt') => new File(['hello'], name, { type: 'text/plain' });

describe('getUploadLimits', () => {
  it('narrow the limits payload and resolve data on success', async () => {
    stubFetch(async () =>
      jsonResponse(200, {
        AnonymousFileLimitBytes: 100,
        AuthenticatedFileLimitBytes: 200,
        AnonymousMaxLifetimeHours: 6,
        AuthenticatedMaxLifetimeHours: 720,
        AnonymousMaxAccessCount: 1,
        AuthenticatedMaxAccessCount: 10
      })
    );

    const result = await getUploadLimits();

    expect(result.data).toEqual({
      AnonymousFileLimitBytes: 100,
      AuthenticatedFileLimitBytes: 200,
      AnonymousMaxLifetimeHours: 6,
      AuthenticatedMaxLifetimeHours: 720,
      AnonymousMaxAccessCount: 1,
      AuthenticatedMaxAccessCount: 10
    });
    expect(result.error).toBeUndefined();
  });

  it('resolve the ProblemDetails body as error on failure', async () => {
    const body = { title: 'Too many requests', detail: 'slow down' };
    stubFetch(async () => jsonResponse(429, body));

    const result = await getUploadLimits();

    expect(result.data).toBeUndefined();
    expect(result.error).toEqual(body);
  });

  it('resolve an error when the payload has an unexpected shape', async () => {
    stubFetch(async () => jsonResponse(200, {}));

    const result = await getUploadLimits();

    expect(result.data).toBeUndefined();
    expect(result.error).toEqual(new Error('Unexpected upload limits response'));
  });
});

describe('createPackage', () => {
  it('posts the request as JSON and returns the created package', async () => {
    const request = {
      Id: 'pkg-1',
      UnlockVerifier: null,
      TimeToLiveHours: 24,
      MaxAccessCount: 1
    };
    const fetchMock = stubFetch(async () => jsonResponse(201, { Id: 'pkg-1', ItemCount: 0 }));

    const result = await createPackage(request);

    expect(result.data).toEqual({ Id: 'pkg-1', ItemCount: 0 });
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:5155/api/v1/content/package');
    expect(init?.method).toBe('POST');
    expect(new Headers(init?.headers).get('Content-Type')).toBe('application/json');
    expect(init?.body).toBe(JSON.stringify(request));
  });

  it('returns the ProblemDetails body on an error response', async () => {
    const body = { title: 'Conflict', detail: 'package already exists' };
    stubFetch(async () => jsonResponse(409, body));

    const result = await createPackage({ Id: 'pkg-1' });

    expect(result.data).toBeUndefined();
    expect(result.error).toEqual(body);
  });
});

describe('getPackageDetails', () => {
  it('fetches the package with the unlock proof and returns status', async () => {
    const body = {
      Package: { Id: 'pkg-1' },
      Items: [],
      AccessSession: { Token: 'session-1', ExpiresAt: '2026-11-04T12:15:00Z' }
    };
    const fetchMock = stubFetch(async () => jsonResponse(200, body));

    const result = await getPackageDetails('pkg-1', 'proof-1', 'session-1');

    expect(result.data).toEqual(body);
    expect(result.status).toBe(200);
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:5155/api/v1/content/package/pkg-1');
    expect(new Headers(init?.headers).get('X-Unlock-Proof')).toBe('proof-1');
    expect(new Headers(init?.headers).get('X-Access-Session')).toBe('session-1');
  });

  it('omits the unlock proof for a plain link', async () => {
    const fetchMock = stubFetch(async () =>
      jsonResponse(200, {
        Package: { Id: 'pkg-1' },
        Items: [],
        AccessSession: { Token: 'session-1', ExpiresAt: '2026-11-04T12:15:00Z' }
      })
    );

    await getPackageDetails('pkg-1');

    expect(new Headers(fetchMock.mock.calls[0][1]?.headers).get('X-Unlock-Proof')).toBeNull();
    expect(new Headers(fetchMock.mock.calls[0][1]?.headers).get('X-Access-Session')).toBeNull();
  });

  it('returns the response status and error for a rejected unlock', async () => {
    const body = { title: 'Unlock required', detail: 'A valid proof is required' };
    stubFetch(async () => jsonResponse(401, body));

    const result = await getPackageDetails('pkg-1', 'wrong-proof');

    expect(result.data).toBeUndefined();
    expect(result.error).toEqual(body);
    expect(result.status).toBe(401);
  });

  it('rejects a success payload with the wrong shape', async () => {
    stubFetch(async () => jsonResponse(200, { Items: [] }));

    const result = await getPackageDetails('pkg-1');

    expect(result.data).toBeUndefined();
    expect(result.error).toEqual({ Items: [] });
    expect(result.status).toBe(200);
  });

  it('rejects a details response whose access session is null', async () => {
    const body = { Package: { Id: 'pkg-1' }, Items: [], AccessSession: null };
    stubFetch(async () => jsonResponse(200, body));

    const result = await getPackageDetails('pkg-1');

    expect(result.data).toBeUndefined();
    expect(result.error).toEqual(body);
    expect(result.status).toBe(200);
  });
});

describe('downloadContent', () => {
  it('downloads through the access session and returns the blob', async () => {
    const fetchMock = stubFetch(
      async () =>
        new Response(new Blob(['file-bytes']), {
          status: 200,
          headers: { 'content-type': 'application/octet-stream' }
        })
    );

    const result = await downloadContent('pkg-1', 'item-1', 'session-1');

    expect(result.data).toBeInstanceOf(Blob);
    expect(result.status).toBe(200);
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:5155/api/v1/content/package/pkg-1/content/item-1/download');
    expect(new Headers(init?.headers).get('X-Access-Session')).toBe('session-1');
  });

  it('returns the ProblemDetails body for a failed download', async () => {
    const body = { title: 'Gone', detail: 'session expired' };
    stubFetch(async () => jsonResponse(404, body));

    const result = await downloadContent('pkg-1', 'item-1', 'stale-session');

    expect(result.data).toBeUndefined();
    expect(result.error).toEqual(body);
    expect(result.status).toBe(404);
  });

  it('does not treat a problem+json error body as file bytes', async () => {
    const body = { title: 'Invalid access session', detail: 'The access session is invalid or has expired' };
    stubFetch(async () => problemResponse(401, body));

    const result = await downloadContent('pkg-1', 'item-1', 'stale-session');

    expect(result.data).toBeUndefined();
    expect(result.error).toEqual(body);
    expect(result.status).toBe(401);
  });

  it('rejects a successful response that is not a blob', async () => {
    stubFetch(async () => jsonResponse(200, { not: 'a blob' }));

    const result = await downloadContent('pkg-1', 'item-1', 'session-1');

    expect(result.error).toEqual(new Error('Download did not return a blob'));
    expect(result.status).toBe(200);
  });
});

describe('listPackages', () => {
  const listItem = {
    Id: 'pkg-1',
    CreatedAt: '2026-11-04T12:00:00Z',
    ExpiresAt: null,
    ItemCount: '2',
    TotalSize: '4096',
    MaxAccessCount: '3',
    CurrentAccessCount: '1',
    RemainingAccesses: '2',
    IsExpired: false,
    IsProtected: true
  };

  it('narrows int64 fields, maps nullable values and keeps the cursor', async () => {
    const fetchMock = stubFetch(async () => jsonResponse(200, { Items: [listItem], NextCursor: 'cursor-2' }));

    const result = await listPackages('cursor-1');

    expect(result.data).toEqual({
      items: [
        {
          Id: 'pkg-1',
          CreatedAt: '2026-11-04T12:00:00Z',
          ExpiresAt: undefined,
          ItemCount: 2,
          TotalSize: 4096,
          MaxAccessCount: 3,
          CurrentAccessCount: 1,
          RemainingAccesses: 2,
          IsExpired: false,
          IsProtected: true
        }
      ],
      nextCursor: 'cursor-2'
    });
    expect(fetchMock.mock.calls[0][0]).toBe('http://localhost:5155/api/v1/content/packages?cursor=cursor-1');
  });

  it('maps a missing cursor to null', async () => {
    stubFetch(async () => jsonResponse(200, { Items: [] }));

    const result = await listPackages();

    expect(result.data).toEqual({ items: [], nextCursor: null });
  });

  it('returns the response status and body on failure', async () => {
    const body = { title: 'Unauthorized', detail: 'sign in first' };
    stubFetch(async () => jsonResponse(401, body));

    const result = await listPackages();

    expect(result.data).toBeUndefined();
    expect(result.error).toEqual(body);
    expect(result.status).toBe(401);
  });

  it('rejects a response whose items do not match the row shape', async () => {
    stubFetch(async () => jsonResponse(200, { Items: [{ Id: 'pkg-1' }] }));

    const result = await listPackages();

    expect(result.data).toBeUndefined();
    expect(result.status).toBe(200);
  });
});

describe('deletePackage', () => {
  it('resolves data on a 204 response', async () => {
    const fetchMock = stubFetch(async () => new Response(null, { status: 204 }));

    const result = await deletePackage('pkg-1');

    expect(result).toMatchObject({ data: true, status: 204 });
    expect(fetchMock.mock.calls[0][0]).toBe('http://localhost:5155/api/v1/content/packages/pkg-1');
    expect(fetchMock.mock.calls[0][1]?.method).toBe('DELETE');
  });

  it('returns the ProblemDetails body when deletion fails', async () => {
    const body = { title: 'Not found', detail: 'package does not exist' };
    stubFetch(async () => jsonResponse(404, body));

    const result = await deletePackage('pkg-1');

    expect(result.data).toBeUndefined();
    expect(result.error).toEqual(body);
    expect(result.status).toBe(404);
  });
});

const uploadRequest = (fileName = 'notes.txt') => ({
  Id: 'item-1',
  Kind: 'file' as const,
  Meta: 'bWV0YQ',
  File: makeFile(fileName),
  FileName: fileName
});

describe('uploadItem', () => {
  beforeEach(() => {
    // oxlint-disable-next-line no-unsafe-type-assertion -- FakeXhr only implements the XHR surface uploadItem uses
    globalThis.XMLHttpRequest = FakeXhr as unknown as typeof XMLHttpRequest;
    globalThis.fetch = vi.fn(async () => {
      throw new Error('XHR path must not use fetch');
    });
    FakeXhr.reset();
  });

  afterEach(() => {
    globalThis.XMLHttpRequest = originalXhr;
    globalThis.fetch = originalFetch;
  });

  it('posts the item fields and bytes to the package upload endpoint', async () => {
    const result = await uploadItem('pkg-1', uploadRequest());

    expect(result.data?.Id).toBe('fake-package');
    expect(result.error).toBeUndefined();
    expect(result.canceled).toBeUndefined();

    const xhr = FakeXhr.sent[0];
    expect(xhr.url.endsWith('/api/v1/content/package/pkg-1/upload')).toBe(true);
    expect(xhr.form?.get('Id')).toBe('item-1');
    expect(xhr.form?.get('Kind')).toBe('file');
    expect(xhr.form?.get('Meta')).toBe('bWV0YQ');
    const entry = xhr.form?.get('File');
    expect(entry).toBeInstanceOf(File);
    if (entry instanceof File) {
      expect(entry.name).toBe('notes.txt');
    }
  });

  it('reports upload progress through the callback', async () => {
    const onProgress = vi.fn();

    await uploadItem('pkg-1', uploadRequest(), onProgress);

    expect(onProgress).toHaveBeenCalledWith(6, 10);
  });

  it('resolve the response body as error on non-2xx status', async () => {
    const body = { title: 'Payload too large', detail: 'file exceeds the limit' };
    FakeXhr.responses = [{ status: 413, body }];

    const result = await uploadItem('pkg-1', uploadRequest());

    expect(result.data).toBeUndefined();
    expect(result.error).toEqual(body);
  });

  it('resolve a network error when the request fails', async () => {
    FakeXhr.responses = [{ status: 0, networkError: true }];

    const result = await uploadItem('pkg-1', uploadRequest());

    expect(result.data).toBeUndefined();
    expect(result.error).toEqual(new Error('Network error during upload'));
  });

  it('rejects a success response with the wrong shape', async () => {
    FakeXhr.responses = [{ status: 200, body: { not: 'a package' } }];

    const result = await uploadItem('pkg-1', uploadRequest());

    expect(result.data).toBeUndefined();
    expect(result.error).toEqual(new Error('Unexpected upload response (status 200)'));
  });

  it('resolves with canceled when the signal aborts before completion', async () => {
    FakeXhr.loadDelayMs = 50;
    const controller = new AbortController();

    const promise = uploadItem('pkg-1', uploadRequest(), undefined, controller.signal);
    // The signal listener is registered once the request is in flight.
    await vi.waitFor(() => expect(FakeXhr.sent).toHaveLength(1));
    controller.abort();

    const result = await promise;

    expect(result.canceled).toBe(true);
    expect(result.data).toBeUndefined();
  });

  it('sends the persisted guest ID as an identity header', async () => {
    window.localStorage.setItem('shareit-guest-id', 'guest-test-1');

    await uploadItem('pkg-1', uploadRequest());

    expect(FakeXhr.sent[0].requestHeaders['X-Guest-ID']).toBe('guest-test-1');
  });
});
