import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { FakeXhr } from '../../tests/fake-xhr.ts';
import { getUploadLimits, uploadFile } from './content.ts';

const originalXhr = globalThis.XMLHttpRequest;
const originalFetch = globalThis.fetch;

const jsonResponse = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });

const stubFetch = (handler: (input: RequestInfo | URL) => Promise<Response>) => {
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
        AnonymousMaxDownloadCount: 1,
        AuthenticatedMaxDownloadCount: 10
      })
    );

    const result = await getUploadLimits();

    expect(result.data).toEqual({
      AnonymousFileLimitBytes: 100,
      AuthenticatedFileLimitBytes: 200,
      AnonymousMaxLifetimeHours: 6,
      AuthenticatedMaxLifetimeHours: 720,
      AnonymousMaxDownloadCount: 1,
      AuthenticatedMaxDownloadCount: 10
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

describe('uploadFile', () => {
  beforeEach(() => {
    // oxlint-disable-next-line no-unsafe-type-assertion -- FakeXhr only implements the XHR surface uploadFile uses
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

  it('posts the file to the package upload endpoint and resolves the response', async () => {
    const file = makeFile();

    const result = await uploadFile('pkg-1', file);

    expect(result.data?.Id).toBe('fake-package');
    expect(result.error).toBeUndefined();
    expect(result.canceled).toBeUndefined();

    const xhr = FakeXhr.sent[0];
    expect(xhr.url.endsWith('/api/v1/content/package/pkg-1/upload')).toBe(true);
    const entry = xhr.form?.get('files');
    expect(entry).toBeInstanceOf(File);
    if (entry instanceof File) {
      expect(entry.name).toBe('notes.txt');
    }
  });

  it('reports upload progress through the callback', async () => {
    const onProgress = vi.fn();

    await uploadFile('pkg-1', makeFile(), onProgress);

    expect(onProgress).toHaveBeenCalledWith(6, 10);
  });

  it('resolve the response body as error on non-2xx status', async () => {
    const body = { title: 'Payload too large', detail: 'file exceeds the limit' };
    FakeXhr.responses = [{ status: 413, body }];

    const result = await uploadFile('pkg-1', makeFile());

    expect(result.data).toBeUndefined();
    expect(result.error).toEqual(body);
  });

  it('resolve a network error when the request fails', async () => {
    FakeXhr.responses = [{ status: 0, networkError: true }];

    const result = await uploadFile('pkg-1', makeFile());

    expect(result.data).toBeUndefined();
    expect(result.error).toEqual(new Error('Network error during upload'));
  });

  it('resolves with canceled when the signal aborts before completion', async () => {
    FakeXhr.loadDelayMs = 50;
    const controller = new AbortController();

    const promise = uploadFile('pkg-1', makeFile(), undefined, controller.signal);
    // The signal listener is registered once the request is in flight.
    await vi.waitFor(() => expect(FakeXhr.sent).toHaveLength(1));
    controller.abort();

    const result = await promise;

    expect(result.canceled).toBe(true);
    expect(result.data).toBeUndefined();
  });

  it('sends the persisted guest ID as an identity header', async () => {
    window.localStorage.setItem('shareit-guest-id', 'guest-test-1');

    await uploadFile('pkg-1', makeFile());

    expect(FakeXhr.sent[0].requestHeaders['X-Guest-ID']).toBe('guest-test-1');
  });
});
