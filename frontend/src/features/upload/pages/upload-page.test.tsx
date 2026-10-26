import { fireEvent, render, screen, waitFor } from '@solidjs/testing-library';
import { flush } from 'solid-js';
import type { Mock } from 'vitest';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { FakeXhr } from '../../../tests/fake-xhr.ts';
import { AuthProvider } from '../../auth/auth-context.tsx';
import { I18nProvider } from '../../../i18n/i18n.tsx';
import { UploadPage } from './upload-page.tsx';

vi.mock('../../../shared/crypto/crypto.ts', () => ({
  encryptText: (text: string) => `enc:${text}`,
  encryptFile: (file: File) => file,
  generatePassphrase: () => 'test-passphrase',
  ENCRYPTED_FILE_OVERHEAD_BYTES: 64
}));

const originalXhr = globalThis.XMLHttpRequest;
const originalFetch = globalThis.fetch;

const jsonResponse = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });

const makeFile = (name: string) => new File(['file-bytes'], name, { type: 'text/plain' });

const fireFileChange = (input: HTMLInputElement, files: File[]) => {
  Object.defineProperty(input, 'files', { value: files, configurable: true });
  fireEvent(input, new Event('change', { bubbles: true }));
};

const renderPage = () =>
  render(() => (
    <I18nProvider>
      <AuthProvider>
        <UploadPage />
      </AuthProvider>
    </I18nProvider>
  ));

describe('<UploadPage />', () => {
  let createPackageRequests: { url: string; body: unknown }[];
  let fetchMock: Mock<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>;

  beforeEach(() => {
    // oxlint-disable-next-line no-unsafe-type-assertion -- FakeXhr only implements the XHR surface uploadFile uses
    globalThis.XMLHttpRequest = FakeXhr as unknown as typeof XMLHttpRequest;
    FakeXhr.reset();
    createPackageRequests = [];

    fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input instanceof Request ? input.url : input);
      if (url.endsWith('/package/limits')) {
        return jsonResponse(200, { AnonymousFileLimitBytes: 1048576, AuthenticatedFileLimitBytes: 1048576 });
      }
      if (url.endsWith('/api/v1/content/package')) {
        createPackageRequests.push({ url, body: init?.body });
        return jsonResponse(200, { Id: 'package-1' });
      }
      throw new Error(`Unexpected fetch: ${url}`);
    });
    globalThis.fetch = fetchMock;
  });

  afterEach(() => {
    globalThis.XMLHttpRequest = originalXhr;
    globalThis.fetch = originalFetch;
  });

  it('shows the validation error without creating a package for an empty form', async () => {
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: 'upload --get-link' }));

    await screen.findByText('Add text or select at least one file before uploading.');
    expect(createPackageRequests).toHaveLength(0);
  });

  it('uploads text and shows the link, passphrase and expiry', async () => {
    renderPage();

    // Anonymous users get no pickers, just fixed short-lifetime notes.
    expect(screen.getByText('content is available for 6h')).toBeInTheDocument();
    expect(screen.getByText('link works once')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /expires:/ })).not.toBeInTheDocument();

    fireEvent.input(screen.getByPlaceholderText('Paste or type your text'), { target: { value: 'hello' } });
    // Solid 2.0 batches writes to the next microtask: drain before the submit
    // click reads the form state.
    flush();
    fireEvent.click(screen.getByRole('button', { name: 'upload --get-link' }));

    await screen.findByText(`${window.location.origin}/s/package-1`);
    expect(screen.getByText('test-passphrase')).toBeInTheDocument();

    expect(createPackageRequests).toHaveLength(1);
    const body = createPackageRequests[0].body;
    if (!(body instanceof FormData)) {
      throw new Error('createPackage must send a FormData body');
    }
    expect(body.getAll('Texts')).toEqual(['enc:hello']);
    expect(body.get('TimeToLiveHours')).toBe('6');
    expect(body.get('MaxDownloadCount')).toBe('1');
  });

  it('retries into the same package after a file upload failure', async () => {
    FakeXhr.responses = [{ status: 500, body: { title: 'failed', detail: 'storage unreachable' } }];

    renderPage();
    fireEvent.input(screen.getByPlaceholderText('Paste or type your text'), { target: { value: 'hello' } });
    const fileInput = document.querySelector<HTMLInputElement>('input[type="file"]')!;
    fireFileChange(fileInput, [makeFile('photo.txt')]);
    flush();

    fireEvent.click(screen.getByRole('button', { name: 'upload --get-link' }));

    // Step 1 succeeded (package created once), step 2 failed -> recovery UI.
    await screen.findByText('storage unreachable');
    expect(createPackageRequests).toHaveLength(1);
    expect(await screen.findByText('retry --files')).toBeInTheDocument();
    expect(screen.getByText('show link anyway')).toBeInTheDocument();

    // Second attempt succeeds and must not create a second package.
    FakeXhr.responses.push({ status: 200, body: { Id: 'package-1' } });
    fireEvent.click(screen.getByRole('button', { name: 'retry --files' }));

    await screen.findByText(`${window.location.origin}/s/package-1`);
    expect(createPackageRequests).toHaveLength(1);
    expect(FakeXhr.sent).toHaveLength(2);
    for (const xhr of FakeXhr.sent) {
      expect(xhr.url.endsWith('/api/v1/content/package/package-1/upload')).toBe(true);
    }
  });

  it('shows the link anyway with a partial notice when the user gives up on the retry', async () => {
    FakeXhr.responses = [{ status: 500, body: { title: 'failed', detail: 'storage unreachable' } }];

    renderPage();
    fireEvent.input(screen.getByPlaceholderText('Paste or type your text'), { target: { value: 'hello' } });
    fireFileChange(document.querySelector<HTMLInputElement>('input[type="file"]')!, [makeFile('photo.txt')]);
    flush();

    fireEvent.click(screen.getByRole('button', { name: 'upload --get-link' }));
    await screen.findByText('storage unreachable');

    fireEvent.click(screen.getByText('show link anyway'));

    await screen.findByText(`${window.location.origin}/s/package-1`);
    expect(screen.getByText('Some files did not finish uploading — the link may be missing them.')).toBeInTheDocument();
    expect(screen.getByText('test-passphrase')).toBeInTheDocument();
  });

  it('cancels a pending upload and offers the recovery options', async () => {
    FakeXhr.loadDelayMs = 300;

    renderPage();
    fireEvent.input(screen.getByPlaceholderText('Paste or type your text'), { target: { value: 'hello' } });
    fireFileChange(document.querySelector<HTMLInputElement>('input[type="file"]')!, [makeFile('photo.txt')]);
    flush();

    fireEvent.click(screen.getByRole('button', { name: 'upload --get-link' }));
    // Wait until the XHR is actually in flight, then cancel it.
    const cancel = await screen.findByText('Cancel');
    await waitFor(() => expect(FakeXhr.sent).toHaveLength(1));
    fireEvent.click(cancel);

    await waitFor(() => expect(screen.getByText('Upload canceled.')).toBeInTheDocument());
    expect(createPackageRequests).toHaveLength(1);
    expect(screen.getByText('retry --files')).toBeInTheDocument();
    expect(screen.getByText('show link anyway')).toBeInTheDocument();
  });
});
