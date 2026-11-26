import { fireEvent, render, screen } from '@solidjs/testing-library';
import { flush } from 'solid-js';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { I18nProvider } from '../../../i18n/i18n.tsx';
import { ComposerPage } from './composer-page.tsx';

const api = vi.hoisted(() => ({
  createPackage: vi.fn(),
  uploadItem: vi.fn(),
  getUploadLimits: vi.fn(),
  resolveShortCode: vi.fn()
}));

vi.mock('../../../shared/api/content.ts', () => api);

const navigateMock = vi.hoisted(() => vi.fn());

vi.mock('@solidjs/router', () => ({
  useNavigate: () => navigateMock
}));

const PACKAGE_ID = '0192f3ab-7cde-7def-8abc-0123456789ab';

const authState = vi.hoisted(() => ({ authenticated: false }));

vi.mock('../../auth/auth-context.tsx', () => ({
  useAuth: () => ({
    isEnabled: true,
    isAuthenticated: () => authState.authenticated
  })
}));

const renderPage = () =>
  render(() => (
    <I18nProvider>
      <ComposerPage />
    </I18nProvider>
  ));

const makeFile = (name = 'photo.jpg') => new File(['file-bytes'], name, { type: 'image/jpeg' });

const addFilesThroughInput = (files: File[]) => {
  const input = document.querySelector<HTMLInputElement>('input[type="file"]');
  if (!input) {
    throw new Error('file input not found');
  }
  Object.defineProperty(input, 'files', { value: files, configurable: true });
  fireEvent(input, new Event('change', { bubbles: true }));
};

const dropFiles = (files: File[]) => {
  const composer = document.querySelector('.composer');
  if (!composer) {
    throw new Error('composer not found');
  }
  const event = new Event('drop', { bubbles: true });
  Object.defineProperty(event, 'dataTransfer', { value: { files } });
  fireEvent(composer, event);
};

describe('<ComposerPage />', () => {
  beforeEach(() => {
    authState.authenticated = false;
    api.getUploadLimits.mockResolvedValue({ data: undefined });
    api.createPackage.mockImplementation(async (request: { Id: string; UseShortCode?: boolean }) => ({
      data: { Id: request.Id, ShortCode: request.UseShortCode ? 'ABC' : null }
    }));
    api.uploadItem.mockResolvedValue({ data: { Id: 'package-1' } });
    api.resolveShortCode.mockResolvedValue({ data: { PackageId: PACKAGE_ID }, status: 200 });
    navigateMock.mockReset();
  });

  it('disables the CTA until there is text or a file', () => {
    renderPage();

    const cta = screen.getByRole('button', { name: 'Create link' });
    expect(cta).toBeDisabled();

    fireEvent.input(screen.getByPlaceholderText('paste or type your text'), { target: { value: 'hello' } });
    flush();

    expect(screen.getByRole('button', { name: 'Create link' })).toBeEnabled();
  });

  it('warns guests that the link will not be listed later', () => {
    renderPage();

    expect(screen.getByText(/can't be listed later/)).toBeInTheDocument();
  });

  it('resolves a normalized code before navigating to the UUID route', async () => {
    renderPage();

    const input = screen.getByLabelText('Have a code?');
    fireEvent.input(input, { target: { value: 'abc-234' } });
    flush();
    fireEvent.click(screen.getByRole('button', { name: 'open' }));

    await vi.waitFor(() =>
      expect(navigateMock).toHaveBeenCalledWith(`/s/${PACKAGE_ID}`, {
        replace: true,
        state: { secret: '234' }
      })
    );
    expect(api.resolveShortCode).toHaveBeenCalledWith('ABC');
  });

  it('shows the server-provided retry wait without disabling the form', async () => {
    api.resolveShortCode.mockResolvedValue({ data: undefined, status: 429, retryAfterSeconds: 90 });
    renderPage();

    const input = screen.getByLabelText('Have a code?');
    fireEvent.input(input, { target: { value: 'ABC234' } });
    flush();
    fireEvent.click(screen.getByRole('button', { name: 'open' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Too many attempts. Try again in 2 min.');
    expect(screen.getByRole('button', { name: 'open' })).toBeEnabled();
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it('shows a generic rate-limit message when no wait time is provided', async () => {
    api.resolveShortCode.mockResolvedValue({ data: undefined, status: 429 });
    renderPage();

    const input = screen.getByLabelText('Have a code?');
    fireEvent.input(input, { target: { value: 'ABC234' } });
    flush();
    fireEvent.click(screen.getByRole('button', { name: 'open' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Too many attempts. Please try again later.');
    expect(screen.getByRole('button', { name: 'open' })).toBeEnabled();
  });

  it('shows a failure when code resolution rejects', async () => {
    api.resolveShortCode.mockRejectedValue(new Error('network down'));
    renderPage();

    const input = screen.getByLabelText('Have a code?');
    fireEvent.input(input, { target: { value: 'ABC234' } });
    flush();
    fireEvent.click(screen.getByRole('button', { name: 'open' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not open this code');
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it('rejects an invalid code before navigating', () => {
    renderPage();

    const input = screen.getByLabelText('Have a code?');
    fireEvent.input(input, { target: { value: 'ABC23O' } });
    flush();
    fireEvent.click(screen.getByRole('button', { name: 'open' }));
    flush();

    expect(navigateMock).not.toHaveBeenCalled();
    expect(screen.getByRole('alert')).toHaveTextContent('Enter a 6-character code.');
  });

  it('does not show the guest listing warning to signed-in users', () => {
    authState.authenticated = true;
    renderPage();

    expect(screen.queryByText(/can't be listed later/)).not.toBeInTheDocument();
  });

  it('adds files through the input, shows their rows and removes them', () => {
    renderPage();
    addFilesThroughInput([makeFile('photo.jpg')]);
    flush();

    expect(screen.getByText('photo.jpg')).toBeInTheDocument();
    expect(screen.getByText('1 file(s)')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'remove photo.jpg' }));
    flush();

    expect(screen.queryByText('photo.jpg')).not.toBeInTheDocument();
  });

  it('accepts files dropped anywhere on the composer', () => {
    renderPage();
    dropFiles([makeFile('dropped.png')]);
    flush();

    expect(screen.getByText('dropped.png')).toBeInTheDocument();
  });

  it('shows only the password row for a guest when the options are expanded', () => {
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /expires in 6 hours/ }));
    flush();

    expect(screen.getByText('password')).toBeInTheDocument();
    expect(screen.queryByText('accesses allowed')).not.toBeInTheDocument();
    expect(screen.getByText(/Expiry and accesses are fixed/)).toBeInTheDocument();
  });

  it('creates the link and shows the result panel', async () => {
    renderPage();
    fireEvent.input(screen.getByPlaceholderText('paste or type your text'), { target: { value: 'hello' } });
    flush();

    fireEvent.click(screen.getByRole('button', { name: 'Create link' }));

    await screen.findByText('link ready');
    expect(api.createPackage).toHaveBeenCalledTimes(1);
    expect(api.uploadItem).toHaveBeenCalledTimes(1);
    const packageId = api.createPackage.mock.calls[0][0].Id;
    expect(screen.getByText(`${window.location.origin}/s/${packageId}`)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Create code' })).not.toBeInTheDocument();
  });

  it('creates a single-use code package and shows only the combined code', async () => {
    renderPage();
    fireEvent.input(screen.getByPlaceholderText('paste or type your text'), { target: { value: 'hello' } });
    flush();

    fireEvent.click(screen.getByRole('button', { name: /expires in 6 hours/ }));
    flush();
    fireEvent.click(screen.getByRole('button', { name: 'code' }));
    flush();

    expect(screen.getByText(/single use \u2022 valid for 10 minutes/i)).toBeInTheDocument();
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Create link' }));

    await screen.findByText(/ABC-[2-9A-HJ-KM-NP-Z]{3}/);
    expect(screen.queryByText(`${window.location.origin}/s/${PACKAGE_ID}`)).not.toBeInTheDocument();
    expect(screen.queryByText('passphrase')).not.toBeInTheDocument();
    expect(screen.getByText(/Single use, valid for 10 minutes/)).toBeInTheDocument();

    const request = api.createPackage.mock.calls[0][0];
    expect(request.UseShortCode).toBe(true);
    expect(request.MaxAccessCount).toBe(1);
    expect(request.TimeToLiveHours).toBe(1);
    expect(api.createPackage.mock.calls[0][1]).toMatch(/^[A-Za-z0-9_-]{43}$/);
  });

  it('uses a static accessible label for upload progress', async () => {
    let resolveUpload: ((result: { data: { Id: string } }) => void) | undefined;
    api.uploadItem.mockImplementation(
      (_packageId, _item, onProgress: (sent: number, total: number) => void) =>
        new Promise((resolve) => {
          onProgress(5, 10);
          resolveUpload = resolve;
        })
    );
    renderPage();
    fireEvent.input(screen.getByPlaceholderText('paste or type your text'), { target: { value: 'hello' } });
    flush();

    fireEvent.click(screen.getByRole('button', { name: 'Create link' }));

    expect(await screen.findByRole('progressbar', { name: 'Upload progress' })).toBeInTheDocument();
    expect(await screen.findByText('Uploading 1/1: 50%')).toBeInTheDocument();

    resolveUpload?.({ data: { Id: 'package-1' } });
    await screen.findByText('link ready');
  });

  it('submits with Ctrl+Enter from the textarea', async () => {
    renderPage();
    const textarea = screen.getByPlaceholderText('paste or type your text');
    fireEvent.input(textarea, { target: { value: 'hello' } });
    flush();

    fireEvent.keyDown(textarea, { key: 'Enter', ctrlKey: true });

    await screen.findByText('link ready');
    expect(api.createPackage).toHaveBeenCalledTimes(1);
  });

  it('lets signed-in users pick expiry and access options', async () => {
    authState.authenticated = true;
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /expires in 7 days/ }));
    flush();

    const selects = screen.getAllByRole('combobox');
    expect(selects).toHaveLength(2);
    fireEvent.change(selects[0], { target: { value: '3' } });
    fireEvent.change(selects[1], { target: { value: '4' } });
    flush();

    expect(screen.getByRole('button', { name: /expires in 30 days/ })).toHaveTextContent('unlimited accesses');

    fireEvent.input(screen.getByPlaceholderText('paste or type your text'), { target: { value: 'hello' } });
    flush();
    fireEvent.click(screen.getByRole('button', { name: 'Create link' }));

    await screen.findByText('link ready');
    const request = api.createPackage.mock.calls[0][0];
    expect(request.TimeToLiveHours).toBe(720);
    expect(request.MaxAccessCount).toBeNull();
  });

  it('validates a custom passphrase before enabling submit', () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /expires in 6 hours/ }));
    flush();
    fireEvent.click(screen.getByRole('button', { name: 'custom' }));
    flush();

    fireEvent.input(screen.getByLabelText('custom'), { target: { value: 'too short' } });
    fireEvent.input(screen.getByPlaceholderText('paste or type your text'), { target: { value: 'hello' } });
    flush();

    expect(screen.getByText('Use at least 12 characters.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create link' })).toBeDisabled();

    fireEvent.input(screen.getByLabelText('custom'), { target: { value: 'long enough passphrase' } });
    flush();

    expect(screen.queryByText('Use at least 12 characters.')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create link' })).toBeEnabled();
  });

  it('shows and regenerates the generated passphrase', () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /expires in 6 hours/ }));
    flush();
    fireEvent.click(screen.getByRole('button', { name: 'generate' }));
    flush();

    const first = screen.getByText(/^[A-Za-z0-9_-]{22}$/).textContent;
    expect(first).toHaveLength(22);

    fireEvent.click(screen.getByRole('button', { name: 'regenerate' }));
    flush();

    expect(screen.getByText(/^[A-Za-z0-9_-]{22}$/).textContent).not.toBe(first);
  });

  it('offers retry and completes after a failed upload', async () => {
    api.uploadItem.mockResolvedValueOnce({ error: { title: 'storage unreachable' } });
    renderPage();
    fireEvent.input(screen.getByPlaceholderText('paste or type your text'), { target: { value: 'hello' } });
    flush();

    fireEvent.click(screen.getByRole('button', { name: 'Create link' }));

    expect(await screen.findByText('storage unreachable')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'retry upload' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'retry upload' }));

    await screen.findByText('link ready');
    expect(api.uploadItem).toHaveBeenCalledTimes(2);
  });

  it('offers only the retry path after a failed upload — no partial link', async () => {
    api.uploadItem.mockResolvedValueOnce({ error: { title: 'storage unreachable' } });
    renderPage();
    fireEvent.input(screen.getByPlaceholderText('paste or type your text'), { target: { value: 'hello' } });
    flush();

    fireEvent.click(screen.getByRole('button', { name: 'Create link' }));
    await screen.findByText('storage unreachable');

    // Links are only shown once every item is uploaded.
    expect(screen.queryByRole('button', { name: 'show link anyway' })).not.toBeInTheDocument();
    expect(screen.queryByText('link ready')).not.toBeInTheDocument();
  });
});
