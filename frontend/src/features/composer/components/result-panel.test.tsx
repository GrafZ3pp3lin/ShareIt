import { fireEvent, render, screen } from '@solidjs/testing-library';
import { describe, expect, it, vi } from 'vitest';

import { I18nProvider } from '../../../i18n/i18n.tsx';
import { ResultPanel } from './result-panel.tsx';
import type { ComposerResult } from '../use-composer.ts';

const PACKAGE_ID = '0192f3ab-7cde-7def-8abc-0123456789ab';
const LINK = `http://localhost:3001/s/${PACKAGE_ID}`;

const baseResult: ComposerResult = {
  link: LINK,
  expiryLabel: '7 days',
  accessesLabel: 'one access',
  passwordLabel: 'no password'
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
  it('shows the UUID link and the no-password note for unprotected uploads', () => {
    renderPanel(baseResult);

    expect(screen.getByText('link ready')).toBeInTheDocument();
    expect(screen.getByText(LINK)).toBeInTheDocument();
    expect(screen.getByText(/Anyone with the link can open it/)).toBeInTheDocument();
    expect(screen.queryByText('passphrase')).not.toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'copy' })).toHaveLength(1);
  });

  it('shows link, passphrase and combined link, and copies each one', async () => {
    const { writeText } = renderPanel({
      ...baseResult,
      passphrase: 'a b&c',
      passwordLabel: 'use generated password',
      linkWithPassphrase: `${LINK}#s=a%20b%26c`
    });

    expect(screen.getByText('a b&c')).toBeInTheDocument();
    expect(screen.getByText(`${LINK}#s=a%20b%26c`)).toBeInTheDocument();
    expect(screen.getByText(/different channels/)).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'copy' })).toHaveLength(3);

    fireEvent.click(screen.getAllByRole('button', { name: 'copy' })[2]);

    await screen.findAllByRole('button', { name: 'copied' });
    expect(writeText).toHaveBeenCalledWith(`${LINK}#s=a%20b%26c`);
  });

  it('puts the formatted code first and hides the link and password in code mode', () => {
    renderPanel({
      ...baseResult,
      code: 'ABC234',
      expiryLabel: '10 minutes',
      passwordLabel: 'use a code'
    });

    expect(screen.getByText('ABC-234')).toBeInTheDocument();
    expect(screen.queryByText(LINK)).not.toBeInTheDocument();
    expect(screen.queryByText('passphrase')).not.toBeInTheDocument();
    expect(screen.queryByText(/Anyone with the link can open it/)).not.toBeInTheDocument();
    expect(screen.getByText(/Single use, valid for 10 minutes/)).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'copy' })).toHaveLength(1);
  });

  it('resets the form', () => {
    const { onReset } = renderPanel(baseResult);

    fireEvent.click(screen.getByRole('button', { name: 'Upload another' }));
    expect(onReset).toHaveBeenCalledTimes(1);
  });
});
