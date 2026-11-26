import { render } from '@solidjs/testing-library';
import { createSignal, flush } from 'solid-js';
import type { Setter } from 'solid-js';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { PackageSummary } from '../../shared/api/content.ts';
import { I18nProvider } from '../../i18n/i18n.tsx';
import { createLinks } from './use-links.ts';

const api = vi.hoisted(() => ({
  listPackages: vi.fn(),
  deletePackage: vi.fn()
}));

vi.mock('../../shared/api/content.ts', () => api);

const authState = vi.hoisted(() => ({
  authenticated: null as (() => boolean) | null,
  login: vi.fn()
}));

vi.mock('../auth/auth-context.tsx', () => ({
  useAuth: () => ({
    isEnabled: true,
    isLoading: () => false,
    isAuthenticated: () => authState.authenticated?.() ?? false,
    login: authState.login
  })
}));

let setAuthenticated: Setter<boolean>;

const makePackage = (id: string, overrides: Partial<PackageSummary> = {}): PackageSummary => ({
  Id: id,
  HasShortCode: true,
  CreatedAt: '2026-11-04T12:00:00Z',
  ExpiresAt: new Date(Date.now() + 6 * 24 * 60 * 60 * 1000).toISOString(),
  ItemCount: 1,
  TotalSize: 1024,
  MaxAccessCount: 1,
  CurrentAccessCount: 0,
  RemainingAccesses: 1,
  IsExpired: false,
  IsProtected: false,
  ...overrides
});

type Links = ReturnType<typeof createLinks>;

const renderLinks = () => {
  let links: Links | undefined;
  const Harness = () => {
    links = createLinks();
    return null;
  };
  render(() => (
    <I18nProvider>
      <Harness />
    </I18nProvider>
  ));
  if (!links) {
    throw new Error('links state was not created');
  }
  return links;
};

describe('createLinks', () => {
  beforeEach(() => {
    const [authenticated, setter] = createSignal(false);
    authState.authenticated = authenticated;
    setAuthenticated = setter;
    api.listPackages.mockReset();
    api.deletePackage.mockReset();
    api.listPackages.mockResolvedValue({ status: 200, data: { items: [], nextCursor: null } });
  });

  it('loads on sign-in and resets the state on sign-out', async () => {
    api.listPackages.mockResolvedValue({
      status: 200,
      data: { items: [makePackage('pkg-1')], nextCursor: 'cursor-1' }
    });
    const links = renderLinks();

    setAuthenticated(true);
    await vi.waitFor(() => expect(links.packages()).toHaveLength(1));
    expect(links.nextCursor()).toBe('cursor-1');

    links.setFilter('active');
    links.requestDelete(links.packages()[0]);
    setAuthenticated(false);
    flush();

    expect(links.packages()).toEqual([]);
    expect(links.nextCursor()).toBeNull();
    expect(links.filter()).toBe('all');
    expect(links.pendingDelete()).toBeNull();
    expect(links.loading()).toBe(false);
  });

  it('ignores a response from an older load', async () => {
    let resolveFirst: ((value: unknown) => void) | undefined;
    api.listPackages
      .mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            resolveFirst = resolve;
          })
      )
      .mockResolvedValueOnce({
        status: 200,
        data: { items: [makePackage('newer')], nextCursor: null }
      });
    const links = renderLinks();

    setAuthenticated(true);
    await vi.waitFor(() => expect(api.listPackages).toHaveBeenCalledTimes(1));

    void links.load();
    await vi.waitFor(() => expect(links.packages().map((item) => item.Id)).toEqual(['newer']));

    resolveFirst?.({ status: 200, data: { items: [makePackage('older')], nextCursor: null } });
    await Promise.resolve();
    flush();

    expect(links.packages().map((item) => item.Id)).toEqual(['newer']);
  });

  it('appends the next page without duplicating already loaded ids', async () => {
    api.listPackages
      .mockResolvedValueOnce({
        status: 200,
        data: { items: [makePackage('pkg-1')], nextCursor: 'cursor-1' }
      })
      .mockResolvedValueOnce({
        status: 200,
        data: { items: [makePackage('pkg-1'), makePackage('pkg-2')], nextCursor: null }
      });
    const links = renderLinks();

    setAuthenticated(true);
    await vi.waitFor(() => expect(links.packages()).toHaveLength(1));

    void links.loadMore();

    await vi.waitFor(() => expect(links.packages()).toHaveLength(2));
    expect(links.packages().map((item) => item.Id)).toEqual(['pkg-1', 'pkg-2']);
    expect(api.listPackages).toHaveBeenCalledTimes(2);
    expect(api.listPackages.mock.calls[1][0]).toBe('cursor-1');
    expect(links.nextCursor()).toBeNull();
  });
});
