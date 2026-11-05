import { fireEvent, render, screen } from '@solidjs/testing-library';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { I18nProvider } from '../../../i18n/i18n.tsx';
import { utf8 } from '../../../shared/crypto/bytes.ts';
import { TextBlock } from './text-block.tsx';
import type { ShareItem } from '../use-share.ts';

const read = vi.hoisted(() => ({
  readShareItem: vi.fn()
}));

vi.mock('../read-item.ts', () => read);

const PACKAGE_ID = '0192f3ab-7cde-7def-8abc-0123456789ab';
const TEXT_ID = '0192f3ab-7cde-7def-8abc-0123456789ac';
const packageKey = new Uint8Array([1, 2, 3]);

const item: ShareItem = {
  Id: TEXT_ID,
  Kind: 'text',
  Size: 11,
  index: 0,
  decodedMeta: { name: null, mime: 'text/plain' },
  displayName: 'content.txt',
  mime: 'text/plain'
};

const renderBlock = () =>
  render(() => (
    <I18nProvider>
      <TextBlock packageId={PACKAGE_ID} item={item} packageKey={packageKey} />
    </I18nProvider>
  ));

describe('<TextBlock />', () => {
  beforeEach(() => {
    read.readShareItem.mockReset();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it('loads the text with the package key and offers a copy action', async () => {
    read.readShareItem.mockResolvedValue(utf8('hello world'));

    renderBlock();

    expect(screen.getByText('Loading text…')).toBeInTheDocument();
    expect(await screen.findByText('hello world')).toBeInTheDocument();
    expect(read.readShareItem).toHaveBeenCalledWith(PACKAGE_ID, item, packageKey);
    expect(screen.getByRole('button', { name: 'copy' })).toBeInTheDocument();
  });

  it('shows a decrypt error and reloads on retry', async () => {
    read.readShareItem.mockRejectedValueOnce(new Error('decrypt failed')).mockResolvedValueOnce(utf8('recovered'));

    renderBlock();

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Could not read this text.');
    expect(alert).toHaveTextContent('decrypt failed');

    fireEvent.click(screen.getByRole('button', { name: 'retry' }));

    expect(await screen.findByText('recovered')).toBeInTheDocument();
    expect(read.readShareItem).toHaveBeenCalledTimes(2);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
