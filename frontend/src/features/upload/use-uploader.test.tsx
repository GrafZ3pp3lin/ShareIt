import { render } from '@solidjs/testing-library';
import { flush } from 'solid-js';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { I18nProvider } from '../../i18n/i18n.tsx';
import { createUploader } from './use-uploader.ts';

// The auth context is mocked so tests can flip between guest and
// authenticated users; everything else stays real.
const authState = vi.hoisted(() => ({ authenticated: false }));

vi.mock('../auth/auth-context.tsx', () => ({
  useAuth: () => ({
    isEnabled: true,
    isAuthenticated: () => authState.authenticated
  })
}));

type Uploader = ReturnType<typeof createUploader>;

const renderUploader = () => {
  let uploader: Uploader | undefined;
  const Harness = () => {
    uploader = createUploader();
    return null;
  };
  render(() => (
    <I18nProvider>
      <Harness />
    </I18nProvider>
  ));
  if (!uploader) {
    throw new Error('uploader was not created');
  }
  return uploader;
};

describe('createUploader expiry', () => {
  beforeEach(() => {
    // A failed limits load is fine; the uploader keeps working without it.
    globalThis.fetch = vi.fn(async () => {
      throw new Error('no limits endpoint in tests');
    });
  });

  afterEach(() => {
    authState.authenticated = false;
    vi.restoreAllMocks();
  });

  it('defaults to 1d for authenticated users and cycles through all options', () => {
    authState.authenticated = true;
    const uploader = renderUploader();

    expect(uploader.expiry().label).toBe('1d');
    uploader.cycleExpiry();
    flush();
    expect(uploader.expiry().label).toBe('7d');
    uploader.cycleExpiry();
    flush();
    expect(uploader.expiry().label).toBe('30d');
    uploader.cycleExpiry();
    flush();
    expect(uploader.expiry().label).toBe('6h');
    uploader.cycleExpiry();
    flush();
    expect(uploader.expiry().label).toBe('1d');
  });

  it('pins guests to the shortest lifetime, ignoring the cycler', () => {
    const uploader = renderUploader();

    expect(uploader.expiry().label).toBe('6h');
    uploader.cycleExpiry();
    flush();
    expect(uploader.expiry().label).toBe('6h');
  });

  it('defaults to unlimited access for authenticated users and cycles through all options', () => {
    authState.authenticated = true;
    const uploader = renderUploader();

    expect(uploader.downloadCount()).toBeNull();
    expect(uploader.accessLabel()).toBe('∞');
    for (const expected of [1, 3, 5, 10, null]) {
      uploader.cycleDownloadCount();
      flush();
      expect(uploader.downloadCount()).toBe(expected);
    }
  });

  it('pins guests to exactly one access', () => {
    const uploader = renderUploader();

    expect(uploader.downloadCount()).toBe(1);
    uploader.cycleDownloadCount();
    flush();
    expect(uploader.downloadCount()).toBe(1);
  });
});
