import { fireEvent, render, screen } from '@solidjs/testing-library';
import { describe, expect, it, vi } from 'vitest';

import { I18nProvider } from '../../i18n/i18n.tsx';
import { CopyButton } from './copy-button.tsx';

const renderButton = (value: string, label?: string) => {
  const writeText = vi.fn(async () => undefined);
  Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
  render(() => (
    <I18nProvider>
      <CopyButton value={value} label={label} />
    </I18nProvider>
  ));
  return { writeText };
};

describe('CopyButton', () => {
  it('copies the value and flips the label to "copied"', async () => {
    const { writeText } = renderButton('https://shareit.example/s/abc');

    fireEvent.click(screen.getByRole('button', { name: 'copy' }));

    await screen.findByRole('button', { name: 'copied' });
    expect(writeText).toHaveBeenCalledWith('https://shareit.example/s/abc');
  });

  it('uses a caller-provided label', () => {
    renderButton('secret', 'Copy passphrase');

    expect(screen.getByRole('button', { name: 'Copy passphrase' })).toBeInTheDocument();
  });

  it('leaves the label untouched when the clipboard write fails', async () => {
    const { writeText } = renderButton('value');
    writeText.mockRejectedValueOnce(new Error('denied'));

    fireEvent.click(screen.getByRole('button', { name: 'copy' }));
    await vi.waitFor(() => expect(writeText).toHaveBeenCalledTimes(1));

    expect(screen.getByRole('button', { name: 'copy' })).toBeInTheDocument();
  });
});
