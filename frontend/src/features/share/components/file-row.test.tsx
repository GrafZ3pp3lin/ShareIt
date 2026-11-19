import { fireEvent, render, screen } from '@solidjs/testing-library';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { I18nProvider } from '../../../i18n/i18n.tsx';
import { utf8 } from '../../../shared/crypto/bytes.ts';
import { seal } from '../../../shared/crypto/framed.ts';
import { deriveKeyBits, frameAad, itemKey, packageSalt, type PackageKey } from '../../../shared/crypto/keys.ts';
import { FileRow } from './file-row.tsx';
import type { ShareItem } from '../use-share.ts';

const read = vi.hoisted(() => ({
  readShareItem: vi.fn(),
  fetchShareItem: vi.fn()
}));

vi.mock('../read-item.ts', () => read);

const PACKAGE_ID = '0192f3ab-7cde-7def-8abc-0123456789ab';
const FILE_ID = '0192f3ab-7cde-7def-8abc-0123456789ac';

const item: ShareItem = {
  Id: FILE_ID,
  Kind: 'file',
  Size: 2048,
  index: 0,
  decodedMeta: { name: 'report.pdf', mime: 'application/pdf' },
  displayName: 'report.pdf',
  mime: 'application/pdf'
};

const renderRow = (
  props: {
    packageKey?: PackageKey;
    onAccessExpired?: (failedToken?: string) => void;
    onPackageGone?: () => void;
  } = {}
) =>
  render(() => (
    <I18nProvider>
      <FileRow
        packageId={PACKAGE_ID}
        item={item}
        packageKey={props.packageKey}
        accessSessionToken="session-1"
        onAccessExpired={props.onAccessExpired}
        onPackageGone={props.onPackageGone}
      />
    </I18nProvider>
  ));

describe('<FileRow />', () => {
  const createObjectURL = vi.fn<(blob: Blob) => string>(() => 'blob:mock');
  const revokeObjectURL = vi.fn();
  let clickSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    read.readShareItem.mockReset();
    read.fetchShareItem.mockReset();
    createObjectURL.mockClear();
    revokeObjectURL.mockClear();
    vi.stubGlobal('URL', { createObjectURL, revokeObjectURL });
    clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
  });

  afterEach(() => {
    clickSpy.mockRestore();
    vi.unstubAllGlobals();
  });

  it('downloads the file lazily, saves the blob and revokes the object URL', async () => {
    read.fetchShareItem.mockResolvedValue(new Response('file-bytes', { status: 200 }));
    renderRow();

    fireEvent.click(screen.getByRole('button', { name: 'download' }));

    await vi.waitFor(() =>
      expect(read.fetchShareItem).toHaveBeenCalledWith(PACKAGE_ID, item, 'session-1', undefined, undefined)
    );
    await vi.waitFor(() => expect(createObjectURL).toHaveBeenCalledTimes(1));
    const blob = createObjectURL.mock.calls[0][0];
    expect(blob).toBeInstanceOf(Blob);
    expect(blob.type).toBe('application/pdf');
    expect(clickSpy).toHaveBeenCalledTimes(1);
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:mock');
    expect(screen.getByRole('button', { name: 'download' })).toBeInTheDocument();
  });

  it('forwards the access-expired and package-gone callbacks to the fetch', async () => {
    const onAccessExpired = vi.fn();
    const onPackageGone = vi.fn();
    read.fetchShareItem.mockRejectedValueOnce(new Error('expired'));
    renderRow({ onAccessExpired, onPackageGone });

    fireEvent.click(screen.getByRole('button', { name: 'download' }));

    await vi.waitFor(() =>
      expect(read.fetchShareItem).toHaveBeenCalledWith(PACKAGE_ID, item, 'session-1', onAccessExpired, onPackageGone)
    );
  });

  it('shows a loading state while the download is in flight', async () => {
    let resolveFetch: ((response: Response) => void) | undefined;
    read.fetchShareItem.mockReturnValue(
      new Promise((resolve) => {
        resolveFetch = resolve;
      })
    );
    renderRow();

    fireEvent.click(screen.getByRole('button', { name: 'download' }));

    const loading = await screen.findByRole('button', { name: 'downloading…' });
    expect(loading).toBeDisabled();

    resolveFetch?.(new Response('file-bytes', { status: 200 }));

    expect(await screen.findByRole('button', { name: 'download' })).toBeEnabled();
  });

  it('keeps the error on the row and retries the same item', async () => {
    read.fetchShareItem
      .mockRejectedValueOnce(new Error('session expired'))
      .mockResolvedValueOnce(new Response('file-bytes', { status: 200 }));
    renderRow();

    fireEvent.click(screen.getByRole('button', { name: 'download' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Download failed.');
    expect(alert).toHaveTextContent('session expired');
    expect(screen.getByRole('button', { name: 'retry' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'retry' }));

    await vi.waitFor(() => expect(read.fetchShareItem).toHaveBeenCalledTimes(2));
    expect(await screen.findByRole('button', { name: 'download' })).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('streams an unencrypted item to a picked file instead of buffering it', async () => {
    const chunks: Uint8Array[] = [];
    const writable = new WritableStream<Uint8Array>({
      write: (chunk) => {
        chunks.push(chunk);
      }
    });
    const showSaveFilePicker = vi.fn(async () => ({ createWritable: async () => writable }));
    vi.stubGlobal('showSaveFilePicker', showSaveFilePicker);
    read.fetchShareItem.mockResolvedValue(new Response('file-bytes', { status: 200 }));

    renderRow();
    fireEvent.click(screen.getByRole('button', { name: 'download' }));

    await vi.waitFor(() =>
      expect(read.fetchShareItem).toHaveBeenCalledWith(PACKAGE_ID, item, 'session-1', undefined, undefined)
    );
    await vi.waitFor(() => expect(chunks).toHaveLength(1));
    expect(new TextDecoder().decode(chunks[0])).toBe('file-bytes');
    expect(showSaveFilePicker).toHaveBeenCalledWith({ suggestedName: 'report.pdf' });
    expect(createObjectURL).not.toHaveBeenCalled();
    expect(await screen.findByRole('button', { name: 'download' })).toBeInTheDocument();
  });

  it('treats a canceled save dialog as a no-op', async () => {
    const canceled = Object.assign(new Error('canceled'), { name: 'AbortError' });
    vi.stubGlobal(
      'showSaveFilePicker',
      vi.fn(async () => {
        throw canceled;
      })
    );

    renderRow();
    fireEvent.click(screen.getByRole('button', { name: 'download' }));

    await vi.waitFor(() => expect(screen.getByRole('button', { name: 'download' })).toBeEnabled());
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(read.fetchShareItem).not.toHaveBeenCalled();
  });

  it('falls back to the buffered save when the picker is blocked', async () => {
    const blocked = Object.assign(new Error('blocked'), { name: 'SecurityError' });
    vi.stubGlobal(
      'showSaveFilePicker',
      vi.fn(async () => {
        throw blocked;
      })
    );
    read.fetchShareItem.mockResolvedValue(new Response('file-bytes', { status: 200 }));

    renderRow();
    fireEvent.click(screen.getByRole('button', { name: 'download' }));

    await vi.waitFor(() => expect(read.fetchShareItem).toHaveBeenCalledTimes(1));
    await vi.waitFor(() => expect(createObjectURL).toHaveBeenCalledTimes(1));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('falls back to the buffered reader when the response has no body', async () => {
    read.fetchShareItem.mockResolvedValue(new Response(null, { status: 200 }));
    read.readShareItem.mockResolvedValue(utf8('file-bytes'));

    renderRow();
    fireEvent.click(screen.getByRole('button', { name: 'download' }));

    await vi.waitFor(() => expect(read.readShareItem).toHaveBeenCalledTimes(1));
    await vi.waitFor(() => expect(createObjectURL).toHaveBeenCalledTimes(1));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('opens the file picker synchronously and decrypts a protected file stream', async () => {
    const packageKey = await deriveKeyBits('correct horse battery', await packageSalt(PACKAGE_ID));
    const sealedBytes = await seal(
      await itemKey(packageKey, FILE_ID),
      utf8('decrypted-file-bytes'),
      frameAad(PACKAGE_ID, FILE_ID, 'item', 0, true)
    );

    const chunks: Uint8Array[] = [];
    const writable = new WritableStream<Uint8Array>({
      write: (chunk) => {
        chunks.push(chunk);
      }
    });
    const showSaveFilePicker = vi.fn(async () => ({ createWritable: async () => writable }));
    vi.stubGlobal('showSaveFilePicker', showSaveFilePicker);
    read.fetchShareItem.mockResolvedValue(new Response(sealedBytes, { status: 200 }));

    renderRow({ packageKey });
    fireEvent.click(screen.getByRole('button', { name: 'download' }));

    // The picker must be invoked synchronously during the click handler before
    // any async key derivation hop can lose user activation.
    expect(showSaveFilePicker).toHaveBeenCalledWith({ suggestedName: 'report.pdf' });

    await vi.waitFor(() => expect(chunks).toHaveLength(1));
    expect(new TextDecoder().decode(chunks[0])).toBe('decrypted-file-bytes');
    expect(createObjectURL).not.toHaveBeenCalled();
    expect(await screen.findByRole('button', { name: 'download' })).toBeInTheDocument();
  });

  it('shows an error and writes nothing when a protected file stream is tampered', async () => {
    const packageKey = await deriveKeyBits('correct horse battery', await packageSalt(PACKAGE_ID));
    const sealedBytes = await seal(
      await itemKey(packageKey, FILE_ID),
      utf8('secret-bytes'),
      frameAad(PACKAGE_ID, FILE_ID, 'item', 0, true)
    );
    const tampered = new Uint8Array(sealedBytes);
    tampered[tampered.length - 1] ^= 0xff;

    read.fetchShareItem.mockResolvedValue(new Response(tampered, { status: 200 }));

    renderRow({ packageKey });
    fireEvent.click(screen.getByRole('button', { name: 'download' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Download failed.');
    expect(alert).toHaveTextContent('Frame decryption failed');
    expect(createObjectURL).not.toHaveBeenCalled();
  });
});
