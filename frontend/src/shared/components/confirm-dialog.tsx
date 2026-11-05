import { Show, createEffect, createUniqueId } from 'solid-js';

type ConfirmDialogProps = {
  open: boolean;
  title: string;
  body?: string;
  confirmLabel: string;
  cancelLabel: string;
  onConfirm: () => void;
  onCancel: () => void;
};

export function ConfirmDialog(props: ConfirmDialogProps) {
  const titleId = createUniqueId();
  const bodyId = createUniqueId();
  let dialog: HTMLDialogElement | undefined;

  createEffect(
    () => props.open,
    (open) => {
      if (!dialog) {
        return;
      }
      if (open && !dialog.open) {
        dialog.showModal();
      } else if (!open && dialog.open) {
        dialog.close();
      }
    }
  );

  return (
    <dialog
      ref={(element) => {
        dialog = element;
      }}
      class="m-auto w-[min(92vw,26rem)] rounded-2xl border border-border-soft bg-panel p-5 text-ink shadow-card backdrop:bg-ink/25"
      aria-labelledby={titleId}
      aria-describedby={props.body ? bodyId : undefined}
      onCancel={(event) => {
        event.preventDefault();
        props.onCancel();
      }}
      onClose={() => {
        if (props.open) {
          props.onCancel();
        }
      }}
    >
      <div class="grid gap-3">
        <h2 id={titleId} class="text-base font-semibold text-ink">
          {props.title}
        </h2>
        <Show when={props.body}>
          {(body) => (
            <p id={bodyId} class="text-sm text-muted">
              {body()}
            </p>
          )}
        </Show>
        <div class="flex flex-wrap justify-end gap-2">
          <button type="button" class="btn btn-secondary" onClick={props.onCancel}>
            {props.cancelLabel}
          </button>
          <button type="button" class="btn btn-primary" onClick={props.onConfirm}>
            {props.confirmLabel}
          </button>
        </div>
      </div>
    </dialog>
  );
}
