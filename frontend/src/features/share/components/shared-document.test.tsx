import { render, screen } from '@solidjs/testing-library';
import { describe, expect, it } from 'vitest';
import type { ContentPackageResponse } from '../../../client/model/index.ts';
import { I18nProvider } from '../../../i18n/i18n.tsx';
import { toBase64Url } from '../../../shared/crypto/base64url.ts';
import { utf8 } from '../../../shared/crypto/bytes.ts';
import { SharedDocument } from './shared-document.tsx';
import type { ShareItem } from '../use-share.ts';

const PACKAGE_ID = '0192f3ab-7cde-7def-8abc-0123456789ab';

const textItem: ShareItem = {
  Id: '0192f3ab-7cde-7def-8abc-0123456789ac',
  Kind: 'text',
  Size: 5,
  Payload: toBase64Url(utf8('hello')),
  index: 0,
  decodedMeta: { name: null, mime: 'text/plain' },
  displayName: 'content.txt',
  mime: 'text/plain'
};

const fileItem: ShareItem = {
  Id: '0192f3ab-7cde-7def-8abc-0123456789ad',
  Kind: 'file',
  Size: 1024,
  Payload: null,
  DownloadToken: 'token-1',
  index: 1,
  decodedMeta: { name: 'report.pdf', mime: 'application/pdf' },
  displayName: 'report.pdf',
  mime: 'application/pdf'
};

const packageDetails = (overrides: Partial<ContentPackageResponse> = {}): ContentPackageResponse => ({
  Id: PACKAGE_ID,
  CreatedAt: '2026-11-04T12:00:00Z',
  ItemCount: 2,
  MaxDownloadCount: 1,
  CurrentDownloadCount: 0,
  RemainingDownloads: null,
  ...overrides
});

const renderDocument = (items: ShareItem[], overrides: Partial<ContentPackageResponse> = {}) =>
  render(() => (
    <I18nProvider>
      <SharedDocument package={packageDetails(overrides)} items={items} packageId={PACKAGE_ID} packageKey={undefined} />
    </I18nProvider>
  ));

describe('<SharedDocument />', () => {
  it('renders text, file rows and the summary for an unprotected package', async () => {
    renderDocument([textItem, fileItem]);

    expect(await screen.findByText('hello')).toBeInTheDocument();
    expect(screen.getByText('report.pdf')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'download' })).toBeInTheDocument();
    expect(screen.getByText('1 text · 1 file · 1.0 KB · unlimited downloads')).toBeInTheDocument();
    expect(screen.getByText(/Unlocking used one access/)).toBeInTheDocument();
  });

  it('uses the singular download label and a relative expiry', () => {
    renderDocument([], {
      ExpiresAt: new Date(Date.now() + 90 * 60 * 1000).toISOString(),
      RemainingDownloads: 1
    });

    expect(screen.getByText('expires in 2 h · 1 download left')).toBeInTheDocument();
  });
});
