import { fireEvent, render, screen } from '@solidjs/testing-library';
import { flush } from 'solid-js';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { I18nProvider } from '../../../i18n/i18n.tsx';
import type { PackageSummary } from '../../../shared/api/content.ts';
import { shortPackageId } from '../link-format.ts';
import { LinksPage } from './links-page.tsx';

const api = vi.hoisted(() => ({
  listPackages: vi.fn(),
  deletePackage: vi.fn()
}));

vi.mock('../../../shared/api/content.ts', () => api);

const authState = vi.hoisted(() => ({
  authenticated: true,
  loading: false,
  enabled: true,
  login: vi.fn()
}));

vi.mock('../../auth/auth-context.tsx', () => ({
  useAuth: () => ({
    isEnabled: authState.enabled,
    isLoading: () => authState.loading,
    isAuthenticated: () => authState.authenticated,
    login: authState.login
  })
}));

const IDS = {
  active: 'ab12cd34-7cde-7def-8abc-0123456789ab',
  usedUp: '9b7e4411-7cde-7def-8abc-0123456789ac',
  expiring: 'c0ffee12-7cde-7def-8abc-0123456789ad'
};

const hoursFromNow = (hours: number) => new Date(Date.now() + hours * 60 * 60 * 1000).toISOString();

const makePackage = (overrides: Partial<PackageSummary>): PackageSummary => ({
  Id: IDS.active,
  CreatedAt: '2026-11-04T14:32:00Z',
  ExpiresAt: hoursFromNow(6 * 24),
  ItemCount: 3,
  TotalSize: 26.4 * 1024 * 1024,
  MaxDownloadCount: 1,
  CurrentDownloadCount: 0,
  RemainingDownloads: 1,
  IsExpired: false,
  IsProtected: true,
  ...overrides
});

const active = makePackage({ Id: IDS.active });
const usedUp = makePackage({
  Id: IDS.usedUp,
  CreatedAt: '2026-11-02T11:05:00Z',
  ItemCount: 1,
  TotalSize: 62 * 1024,
  CurrentDownloadCount: 1,
  RemainingDownloads: 0,
  IsProtected: false
});
const expiring = makePackage({
  Id: IDS.expiring,
  CreatedAt: '2026-11-03T18:47:00Z',
  ExpiresAt: hoursFromNow(12),
  ItemCount: 2,
  TotalSize: 1024,
  RemainingDownloads: 3
});

const renderPage = () =>
  render(() => (
    <I18nProvider>
      <LinksPage />
    </I18nProvider>
  ));

describe('<LinksPage />', () => {
  beforeEach(() => {
    authState.authenticated = true;
    authState.loading = false;
    authState.enabled = true;
    api.listPackages.mockResolvedValue({ status: 200, data: { items: [active, usedUp, expiring], nextCursor: null } });
    api.deletePackage.mockResolvedValue({ status: 204, data: true });
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it('renders the owner links with their derived states', async () => {
    renderPage();

    await screen.findByText('Your links');
    expect(api.listPackages).toHaveBeenCalledTimes(1);

    expect(screen.getByText(shortPackageId(IDS.active))).toBeInTheDocument();
    expect(screen.getAllByText('active').length).toBeGreaterThan(0);
    expect(screen.getAllByText('expiring').length).toBeGreaterThan(0);
    expect(screen.getByText('used up')).toBeInTheDocument();
    expect(screen.getAllByText('protected').length).toBeGreaterThan(0);
    expect(screen.getByText('no passphrase')).toBeInTheDocument();
    expect(screen.getByText(/1 of 1 access used/)).toBeInTheDocument();

    const copyButtons = screen.getAllByRole('button', { name: 'copy link' });
    expect(copyButtons[0]).toBeEnabled();
    expect(copyButtons[1]).toBeDisabled();
    expect(copyButtons[2]).toBeEnabled();
  });

  it('filters client-side, including used-up links in the expired bucket', async () => {
    renderPage();
    await screen.findByText('Your links');

    fireEvent.click(screen.getByRole('button', { name: /expired/ }));
    flush();

    expect(screen.getByText(shortPackageId(IDS.usedUp))).toBeInTheDocument();
    expect(screen.queryByText(shortPackageId(IDS.active))).not.toBeInTheDocument();
    expect(screen.queryByText(shortPackageId(IDS.expiring))).not.toBeInTheDocument();
  });

  it('deletes a link after confirmation and removes the row', async () => {
    renderPage();
    await screen.findByText('Your links');

    fireEvent.click(screen.getAllByRole('button', { name: 'delete' })[0]);
    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveTextContent('Delete “');

    fireEvent.click(screen.getByRole('button', { name: 'Delete package' }));

    await vi.waitFor(() => expect(api.deletePackage).toHaveBeenCalledWith(IDS.active));
    expect(screen.queryByText(shortPackageId(IDS.active))).not.toBeInTheDocument();
  });

  it('shows the load error and retries the request', async () => {
    api.listPackages.mockResolvedValueOnce({ status: 500, error: { detail: 'storage unreachable' } });
    api.listPackages.mockResolvedValueOnce({ status: 200, data: { items: [active], nextCursor: null } });

    renderPage();

    expect(await screen.findByText('Unable to load your links')).toBeInTheDocument();
    expect(screen.getByText('storage unreachable')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'retry' }));

    await screen.findByText('Your links');
    expect(api.listPackages).toHaveBeenCalledTimes(2);
  });

  it('shows the empty state for an owner with no links', async () => {
    api.listPackages.mockResolvedValue({ status: 200, data: { items: [], nextCursor: null } });

    renderPage();

    expect(await screen.findByText('No links yet')).toBeInTheDocument();
    expect(screen.getByText('Create your first link').closest('a')).not.toBeNull();
  });

  it('shows the login gate and skips the request for guests', () => {
    authState.authenticated = false;

    renderPage();

    expect(screen.getByText('Sign in to see your links')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument();
    expect(api.listPackages).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(authState.login).toHaveBeenCalledTimes(1);
  });
});
