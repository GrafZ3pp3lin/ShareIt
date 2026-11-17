import { fireEvent, render, screen } from '@solidjs/testing-library';
import { describe, expect, it, vi } from 'vitest';

import { I18nProvider } from '../../../i18n/i18n.tsx';
import { ResultPanel } from './result-panel.tsx';
import type { ComposerResult } from '../use-composer.ts';

const baseResult: ComposerResult = {
  link: 'http://localhost:3001/s/package-1',
  expiryLabel: '7 days',
  accessesLabel: 'once',
  passwordLabel: 'off',
  encrypted: false
};

const renderPanel = (result: ComposerResult) => {
  const writeText = vi.fn(async () => undefined);
  Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
  const onReset = vi.fn();
  render(() => (
    <I18nProvider>
      <ResultPanel result={result} onReset={onReset} />
    </I18nProvider>
  ));
  return { writeText, onReset };
};

describe('ResultPanel', () => {
  it('shows the link and the no-password note for unprotected uploads', () => {
    renderPanel(baseResult);

    expect(screen.getByText('link ready')).toBeInTheDocument();
    expect(screen.getByText('http://localhost:3001/s/package-1')).toBeInTheDocument();
    expect(screen.getByText(/Anyone with the link can open it/)).toBeInTheDocument();
    expect(screen.queryByText('passphrase')).not.toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'copy' })).toHaveLength(1);
  });

  it('shows link, passphrase and combined link, and copies each one', async () => {
    const { writeText } = renderPanel({
      ...baseResult,
      passphrase: 'a b&c',
      passwordLabel: 'generated',
      encrypted: true,
      linkWithPassphrase: 'http://localhost:3001/s/package-1#s=a%20b%26c'
    });

    expect(screen.getByText('a b&c')).toBeInTheDocument();
    expect(screen.getByText('http://localhost:3001/s/package-1#s=a%20b%26c')).toBeInTheDocument();
    expect(screen.getByText(/different channels/)).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'copy' })).toHaveLength(3);

    fireEvent.click(screen.getAllByRole('button', { name: 'copy' })[2]);

    await screen.findAllByRole('button', { name: 'copied' });
    expect(writeText).toHaveBeenCalledWith('http://localhost:3001/s/package-1#s=a%20b%26c');
  });

  it('shows the incomplete notice and resets the form', () => {
    const { onReset } = renderPanel({ ...baseResult, incomplete: true });

    expect(screen.getByText(/Some items did not finish uploading/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Upload another' }));
    expect(onReset).toHaveBeenCalledTimes(1);
  });
});
