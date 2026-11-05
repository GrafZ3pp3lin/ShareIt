import { fireEvent, render, screen } from '@solidjs/testing-library';
import { flush } from 'solid-js';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ContentPackageDetailsResponse } from '../../../client/model/index.ts';
import { I18nProvider } from '../../../i18n/i18n.tsx';
import { toBase64Url } from '../../../shared/crypto/base64url.ts';
import { utf8 } from '../../../shared/crypto/bytes.ts';
import { deriveKeyBits, itemAad, itemKey, packageSalt } from '../../../shared/crypto/keys.ts';
import { buildMeta, encodeMeta } from '../../../shared/crypto/meta.ts';
import { seal } from '../../../shared/crypto/sealed.ts';
import { SharePage } from './share-page.tsx';

const api = vi.hoisted(() => ({
  getPackageDetails: vi.fn(),
  downloadContent: vi.fn()
}));

vi.mock('../../../shared/api/content.ts', () => api);

const params = vi.hoisted(() => ({ packageId: '' }));

vi.mock('@solidjs/router', () => ({
  useParams: () => ({ packageId: params.packageId })
}));

const PACKAGE_ID = '0192f3ab-7cde-7def-8abc-0123456789ab';
const TEXT_ID = '0192f3ab-7cde-7def-8abc-0123456789ac';
const FILE_ID = '0192f3ab-7cde-7def-8abc-0123456789ad';

const packageDetails = (): ContentPackageDetailsResponse => ({
  Package: {
    Id: PACKAGE_ID,
    CreatedAt: new Date().toISOString(),
    ItemCount: 2,
    MaxDownloadCount: 1,
    CurrentDownloadCount: 1,
    RemainingDownloads: 0,
    ExpiresAt: new Date(Date.now() + 6 * 24 * 60 * 60 * 1000).toISOString()
  },
  Items: [
    {
      Id: TEXT_ID,
      Kind: 'text',
      Size: 5,
      Meta: toBase64Url(buildMeta({ name: null, mime: 'text/plain' })),
      Payload: toBase64Url(utf8('hello'))
    },
    {
      Id: FILE_ID,
      Kind: 'file',
      Size: 12,
      Meta: toBase64Url(buildMeta({ name: 'photo.jpg', mime: 'image/jpeg' })),
      Payload: toBase64Url(utf8('file-bytes'))
    }
  ]
});

const protectedPackageDetails = async (passphrase: string): Promise<ContentPackageDetailsResponse> => {
  const key = await deriveKeyBits(passphrase, await packageSalt(PACKAGE_ID));
  const textMeta = await encodeMeta({ name: null, mime: 'text/plain' }, key, PACKAGE_ID, TEXT_ID);
  const fileMeta = await encodeMeta({ name: 'secret.pdf', mime: 'application/pdf' }, key, PACKAGE_ID, FILE_ID);
  const textBytes = await seal(await itemKey(key, TEXT_ID), utf8('sealed hello'), itemAad(PACKAGE_ID, TEXT_ID));
  const fileBytes = await seal(await itemKey(key, FILE_ID), utf8('file-bytes'), itemAad(PACKAGE_ID, FILE_ID));

  return {
    ...packageDetails(),
    Items: [
      {
        Id: TEXT_ID,
        Kind: 'text',
        Size: textBytes.length,
        Meta: toBase64Url(textMeta),
        Payload: toBase64Url(textBytes)
      },
      {
        Id: FILE_ID,
        Kind: 'file',
        Size: fileBytes.length,
        Meta: toBase64Url(fileMeta),
        Payload: toBase64Url(fileBytes)
      }
    ]
  };
};

const renderPage = () =>
  render(() => (
    <I18nProvider>
      <SharePage />
    </I18nProvider>
  ));

const submitPassphrase = async (value: string) => {
  fireEvent.input(screen.getByPlaceholderText('passphrase'), { target: { value } });
  flush();
  fireEvent.click(screen.getByRole('button', { name: 'Unlock' }));
};

describe('<SharePage />', () => {
  beforeEach(() => {
    params.packageId = PACKAGE_ID;
    window.history.replaceState(null, '', `/s/${PACKAGE_ID}`);
    api.getPackageDetails.mockReset();
    api.downloadContent.mockReset();
    api.getPackageDetails.mockResolvedValue({ status: 200, data: packageDetails() });
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it('renders the gate and makes no request before the visitor submits', () => {
    renderPage();
    flush();

    expect(screen.getByText('This link is protected')).toBeInTheDocument();
    expect(screen.getByPlaceholderText('passphrase')).toBeInTheDocument();
    expect(api.getPackageDetails).not.toHaveBeenCalled();
  });

  it('sends the derived proof and renders the document after unlock', async () => {
    renderPage();
    await submitPassphrase('correct horse battery');

    await screen.findByText('Shared content');

    expect(api.getPackageDetails).toHaveBeenCalledTimes(1);
    const [packageId, proof] = api.getPackageDetails.mock.calls[0];
    expect(packageId).toBe(PACKAGE_ID);
    expect(proof).toMatch(/^[A-Za-z0-9_-]{43}$/);

    await screen.findByText('hello');
    expect(screen.getByText('photo.jpg')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'download' })).toBeInTheDocument();
  });

  it('opens sealed metadata and content with the package key', async () => {
    const passphrase = 'correct horse battery';
    api.getPackageDetails.mockResolvedValueOnce({ status: 200, data: await protectedPackageDetails(passphrase) });

    renderPage();
    await submitPassphrase(passphrase);

    await screen.findByText('sealed hello');
    expect(screen.getByText('secret.pdf')).toBeInTheDocument();
  });

  it('opens an unprotected package when the gate is submitted empty', async () => {
    renderPage();
    await submitPassphrase('');

    await screen.findByText('Shared content');
    expect(api.getPackageDetails).toHaveBeenCalledTimes(1);
    expect(api.getPackageDetails.mock.calls[0][1]).toBeUndefined();
  });

  it('keeps the gate and explains a wrong passphrase without revealing content', async () => {
    api.getPackageDetails.mockResolvedValueOnce({
      status: 401,
      error: { title: 'Unlock required', detail: 'A valid unlock proof is required to view this package.' }
    });

    renderPage();
    await submitPassphrase('wrong passphrase');

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent("That passphrase doesn't work.");
    expect(alert).toHaveTextContent('no access was used');
    expect(screen.queryByText('Shared content')).not.toBeInTheDocument();
  });

  it('unlocks immediately from the #s= fragment and drops the fragment', async () => {
    window.history.replaceState(null, '', `/s/${PACKAGE_ID}#s=${encodeURIComponent('correct horse battery')}`);

    renderPage();

    // The fragment starts in the unlocking state: no gate flash.
    expect(screen.queryByText('This link is protected')).not.toBeInTheDocument();

    await screen.findByText('Shared content');

    expect(api.getPackageDetails).toHaveBeenCalledTimes(1);
    expect(api.getPackageDetails.mock.calls[0][1]).toMatch(/^[A-Za-z0-9_-]{43}$/);
    expect(window.location.hash).toBe('');
    expect(screen.queryByText('This link is protected')).not.toBeInTheDocument();
  });

  it('keeps the access-used copy for a rate-limited unlock', async () => {
    api.getPackageDetails.mockResolvedValueOnce({
      status: 429,
      error: { title: 'Too many requests', detail: 'Too many attempts for this link.' }
    });

    renderPage();
    await submitPassphrase('');

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Too many attempts.');
    expect(alert).toHaveTextContent('No access was used.');
    expect(screen.queryByText('Shared content')).not.toBeInTheDocument();
  });

  it('shows the gone state when the package is expired or used up', async () => {
    api.getPackageDetails.mockResolvedValueOnce({
      status: 410,
      error: { title: 'Package access limit reached', detail: 'The maximum number of accesses has been reached' }
    });

    renderPage();
    await submitPassphrase('');

    expect(await screen.findByText('This link is no longer available')).toBeInTheDocument();
    expect(screen.queryByPlaceholderText('passphrase')).not.toBeInTheDocument();
  });
});
