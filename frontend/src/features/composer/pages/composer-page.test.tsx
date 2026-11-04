import { fireEvent, render, screen } from '@solidjs/testing-library';
import { flush } from 'solid-js';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { I18nProvider } from '../../../i18n/i18n.tsx';
import { ComposerPage } from './composer-page.tsx';

const api = vi.hoisted(() => ({
  createPackage: vi.fn(),
  uploadItem: vi.fn(),
  getUploadLimits: vi.fn()
}));

vi.mock('../../../shared/api/content.ts', () => api);

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
    api.createPackage.mockImplementation(async (request: { Id: string }) => ({ data: { Id: request.Id } }));
    api.uploadItem.mockResolvedValue({ data: { Id: 'package-1' } });
  });

  it('disables the CTA until there is text or a file', () => {
    renderPage();

    const cta = screen.getByRole('button', { name: 'Create link' });
    expect(cta).toBeDisabled();

    fireEvent.input(screen.getByPlaceholderText('paste or type your text'), { target: { value: 'hello' } });
    flush();

    expect(screen.getByRole('button', { name: 'Create link' })).toBeEnabled();
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

    fireEvent.click(screen.getByRole('button', { name: /expires 6 hours/ }));
    flush();

    expect(screen.getByText('password')).toBeInTheDocument();
    expect(screen.queryByText('downloads allowed')).not.toBeInTheDocument();
    expect(screen.getByText(/Expiry and downloads are fixed/)).toBeInTheDocument();
  });

  it('creates the link and shows the result panel', async () => {
    renderPage();
    fireEvent.input(screen.getByPlaceholderText('paste or type your text'), { target: { value: 'hello' } });
    flush();

    fireEvent.click(screen.getByRole('button', { name: 'Create link' }));

    await screen.findByText('link ready');
    expect(api.createPackage).toHaveBeenCalledTimes(1);
    expect(api.uploadItem).toHaveBeenCalledTimes(1);
    expect(
      screen.getByText(`${window.location.origin}/s/${api.createPackage.mock.calls[0][0].Id}`)
    ).toBeInTheDocument();
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
});
