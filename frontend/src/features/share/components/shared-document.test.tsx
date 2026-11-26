import { render, screen } from '@solidjs/testing-library';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ContentPackageResponse } from '../../../client/model/index.ts';
import { I18nProvider } from '../../../i18n/i18n.tsx';
import { utf8 } from '../../../shared/crypto/bytes.ts';
import { SharedDocument } from './shared-document.tsx';
import type { ShareItem } from '../use-share.ts';

const api = vi.hoisted(() => ({
  downloadContent: vi.fn()
}));

vi.mock('../../../shared/api/content.ts', () => api);

const PACKAGE_ID = '0192f3ab-7cde-7def-8abc-0123456789ab';

const textItem: ShareItem = {
  Id: '0192f3ab-7cde-7def-8abc-0123456789ac',
  Kind: 'text',
  Size: 5,
  index: 0,
  decodedMeta: { name: null, mime: 'text/plain' },
  displayName: 'content.txt',
  mime: 'text/plain'
};

const fileItem: ShareItem = {
  Id: '0192f3ab-7cde-7def-8abc-0123456789ad',
  Kind: 'file',
  Size: 1024,
  index: 1,
  decodedMeta: { name: 'report.pdf', mime: 'application/pdf' },
  displayName: 'report.pdf',
  mime: 'application/pdf'
};

const packageDetails = (overrides: Partial<ContentPackageResponse> = {}): ContentPackageResponse => ({
  Id: PACKAGE_ID,
  CreatedAt: '2026-11-04T12:00:00Z',
  ItemCount: 2,
  MaxAccessCount: 1,
  CurrentAccessCount: 0,
  RemainingAccesses: null,
  ExpiresAt: new Date(Date.now() + 6 * 24 * 60 * 60 * 1000).toISOString(),
  IsProtected: false,
  ...overrides
});

const renderDocument = (items: ShareItem[], overrides: Partial<ContentPackageResponse> = {}) =>
  render(() => (
    <I18nProvider>
      <SharedDocument
        package={packageDetails(overrides)}
        items={items}
        packageId={PACKAGE_ID}
        packageKey={undefined}
        accessSessionToken="session-1"
        accessSessionExpiresAt={new Date(Date.now() + 15 * 60 * 1000).toISOString()}
        onAccessExpired={() => undefined}
        onPackageGone={() => undefined}
      />
    </I18nProvider>
  ));

describe('<SharedDocument />', () => {
  beforeEach(() => {
    api.downloadContent.mockReset();
    api.downloadContent.mockResolvedValue({ data: new Blob([utf8('hello')]), status: 200 });
  });

  it('renders text, file rows and the summary for an unprotected package', async () => {
    renderDocument([textItem, fileItem]);

    expect(await screen.findByText('hello')).toBeInTheDocument();
    expect(screen.getByText('report.pdf')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'download' })).toBeInTheDocument();
    expect(screen.getByText('1 text · 1 file · 1.0 KB · expires in 6 days')).toBeInTheDocument();
    expect(screen.getByText(/This access is valid until \d{1,2}:\d{2}/)).toBeInTheDocument();
    expect(api.downloadContent).toHaveBeenCalledWith(PACKAGE_ID, textItem.Id, 'session-1');
  });

  it('shows a relative expiry without exposing remaining accesses', () => {
    renderDocument([], {
      ExpiresAt: new Date(Date.now() + 90 * 60 * 1000).toISOString(),
      RemainingAccesses: 1
    });

    expect(screen.getByText('expires in 2 h')).toBeInTheDocument();
    expect(screen.queryByText(/access left/)).not.toBeInTheDocument();
  });
});
