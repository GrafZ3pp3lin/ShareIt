import { fireEvent, render, screen } from '@solidjs/testing-library';
import { createSignal } from 'solid-js';
import { describe, expect, it, vi } from 'vitest';

import { ConfirmDialog } from './confirm-dialog.tsx';

const renderDialog = (initialOpen: boolean) => {
  const [open, setOpen] = createSignal(initialOpen);
  const onConfirm = vi.fn();
  const onCancel = vi.fn();
  render(() => (
    <ConfirmDialog
      open={open()}
      title="Delete link?"
      body="This cannot be undone."
      confirmLabel="Delete"
      cancelLabel="Cancel"
      onConfirm={onConfirm}
      onCancel={onCancel}
    />
  ));
  return { setOpen, onConfirm, onCancel };
};

describe('ConfirmDialog', () => {
  it('opens as a modal labelled by its title', async () => {
    const { setOpen } = renderDialog(false);

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    setOpen(true);

    const dialog = await screen.findByRole('dialog', { name: 'Delete link?' });
    expect(dialog).toHaveAttribute('open');
    expect(dialog).toHaveAccessibleDescription('This cannot be undone.');
    expect(screen.getByText('This cannot be undone.')).toBeInTheDocument();
  });

  it('calls onConfirm and onCancel from the actions', () => {
    const { onConfirm, onCancel } = renderDialog(true);

    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    expect(onConfirm).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });
});
