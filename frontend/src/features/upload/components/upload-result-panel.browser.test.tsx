import { fireEvent, render, screen } from '@solidjs/testing-library';
import { describe, expect, it, vi } from 'vitest';

import { UploadResultPanel } from './upload-result-panel.tsx';
import type { UploadResult } from './upload-types.ts';

const baseResult: UploadResult = {
  link: 'http://localhost:3001/s/package-1',
  expiryLabel: '1 hour',
  encrypted: false
};

const renderPanel = (result: UploadResult, props: { partialLabel?: string } = {}) => {
  const onCopyLink = vi.fn();
  const onCopyPassphrase = vi.fn();
  const onReset = vi.fn();
  const rendered = render(() => (
    <UploadResultPanel
      result={result}
      headingLabel="link ready"
      caption="expires 1 hour"
      copyLabel="copy"
      copiedLabel="copied"
      passphraseLabel="Passphrase (shown once):"
      copyPassphraseLabel="Copy passphrase"
      anotherLabel="Upload another"
      partialLabel={props.partialLabel}
      onCopyLink={onCopyLink}
      onCopyPassphrase={onCopyPassphrase}
      onReset={onReset}
    />
  ));
  return { onCopyLink, onCopyPassphrase, onReset, ...rendered };
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

  it('reports a link copy through the callback and flips the button label', () => {
    const { onCopyLink } = renderPanel(baseResult);

    fireEvent.click(screen.getByRole('button', { name: 'copy' }));

    expect(onCopyLink).toHaveBeenCalledWith(baseResult.link);
    expect(screen.getByRole('button', { name: 'copied' })).toBeInTheDocument();
  });

  it('shows and copies the passphrase when one is set', () => {
    const { onCopyPassphrase } = renderPanel({ ...baseResult, passphrase: 'correct horse', encrypted: true });

    expect(screen.getByText('correct horse')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Copy passphrase' }));

    expect(onCopyPassphrase).toHaveBeenCalledWith('correct horse');
  });

  it('hides the passphrase section for plain uploads', () => {
    renderPanel(baseResult);

    expect(screen.queryByText('Passphrase (shown once):')).not.toBeInTheDocument();
  });

  it('shows the partial notice only for incomplete uploads', () => {
    renderPanel({ ...baseResult, incomplete: true }, { partialLabel: 'Some files did not finish uploading.' });

    expect(screen.getByText('Some files did not finish uploading.')).toBeInTheDocument();
  });

  it('hides the partial notice for complete uploads', () => {
    renderPanel(baseResult, { partialLabel: 'Some files did not finish uploading.' });

    expect(screen.queryByText('Some files did not finish uploading.')).not.toBeInTheDocument();
  });
});
