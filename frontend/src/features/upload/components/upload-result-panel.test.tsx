import { fireEvent, render, screen } from '@solidjs/testing-library';
import { describe, expect, it, vi } from 'vitest';

import { I18nProvider } from '../../../i18n/i18n.tsx';
import { UploadResultPanel } from './upload-result-panel.tsx';
import type { UploadResult } from './upload-types.ts';

const baseResult: UploadResult = {
  link: 'http://localhost:3001/s/package-1',
  expiryLabel: '1 hour',
  encrypted: false
};

// Translation labels come from the en dictionary via the shared I18nProvider.
const renderPanel = (result: UploadResult) => {
  const writeText = vi.fn(async () => undefined);
  Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
  const onReset = vi.fn();
  const rendered = render(() => (
    <I18nProvider>
      <UploadResultPanel result={result} onReset={onReset} />
    </I18nProvider>
  ));
  return { writeText, onReset, ...rendered };
};

describe('UploadResultPanel', () => {
  it('renders the link, heading, caption and reset action', () => {
    const { onReset } = renderPanel(baseResult);

    expect(screen.getByText('link ready')).toBeInTheDocument();
    expect(screen.getByText('http://localhost:3001/s/package-1')).toBeInTheDocument();
    expect(screen.getByText('expires 1 hour')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Upload another' }));
    expect(onReset).toHaveBeenCalledTimes(1);
  });

  it('writes the link to the clipboard and flips the button label', async () => {
    const { writeText } = renderPanel(baseResult);

    fireEvent.click(screen.getByRole('button', { name: 'copy' }));

    await screen.findByRole('button', { name: 'copied' });
    expect(writeText).toHaveBeenCalledWith(baseResult.link);
  });

  it('writes the passphrase to the clipboard when one is set', async () => {
    const { writeText } = renderPanel({ ...baseResult, passphrase: 'correct horse', encrypted: true });

    expect(screen.getByText('correct horse')).toBeInTheDocument();
    expect(screen.getByText('Passphrase (shown once):')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Copy passphrase' }));

    await screen.findByRole('button', { name: 'copied' });
    expect(writeText).toHaveBeenCalledWith('correct horse');
  });

  it('keeps the copy button untouched when the clipboard write fails', async () => {
    const { writeText } = renderPanel(baseResult);
    writeText.mockRejectedValueOnce(new Error('denied'));

    fireEvent.click(screen.getByRole('button', { name: 'copy' }));
    await vi.waitFor(() => expect(writeText).toHaveBeenCalledTimes(1));

    expect(screen.getByRole('button', { name: 'copy' })).toBeInTheDocument();
  });

  it('hides the passphrase section for plain uploads', () => {
    renderPanel(baseResult);

    expect(screen.queryByText('Passphrase (shown once):')).not.toBeInTheDocument();
  });

  it('shows the partial notice only for incomplete uploads', () => {
    renderPanel({ ...baseResult, incomplete: true });

    expect(screen.getByText('Some files did not finish uploading — the link may be missing them.')).toBeInTheDocument();
  });

  it('hides the partial notice for complete uploads', () => {
    renderPanel(baseResult);

    expect(
      screen.queryByText('Some files did not finish uploading — the link may be missing them.')
    ).not.toBeInTheDocument();
  });
});
