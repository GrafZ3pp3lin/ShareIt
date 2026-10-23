import { Show, createSignal } from 'solid-js';
import IconCheck from '~icons/lucide/check';
import { QrPlaceholder } from './qr-placeholder.tsx';
import type { UploadResult } from './upload-types.ts';

type UploadResultPanelProps = {
  result: UploadResult;
  headingLabel: string;
  caption: string;
  copyLabel: string;
  copiedLabel: string;
  passphraseLabel: string;
  copyPassphraseLabel: string;
  anotherLabel: string;
  /** Shown when the link points at a package whose file uploads did not all finish. */
  partialLabel?: string;
  onCopyLink: (link: string) => void;
  onCopyPassphrase: (passphrase: string) => void;
  onReset: () => void;
};

export function UploadResultPanel(props: UploadResultPanelProps) {
  const [copiedLink, setCopiedLink] = createSignal(false);
  const [copiedPassphrase, setCopiedPassphrase] = createSignal(false);

  const copyLink = () => {
    props.onCopyLink(props.result.link);
    setCopiedLink(true);
    setTimeout(() => setCopiedLink(false), 1500);
  };

  const copyPassphrase = (passphrase: string) => {
    props.onCopyPassphrase(passphrase);
    setCopiedPassphrase(true);
    setTimeout(() => setCopiedPassphrase(false), 1500);
  };

  return (
    <div class="grid gap-4 font-mono">
      <div class="flex items-center gap-2 text-xs text-brand-deep">
        <IconCheck class="h-3.5 w-3.5" />
        <span>{props.headingLabel}</span>
      </div>

      <div class="flex items-center gap-3 rounded-lg border border-border-soft bg-panel-soft/45 px-3 py-2.5">
        <span class="flex-1 overflow-hidden text-sm text-ink text-ellipsis whitespace-nowrap">{props.result.link}</span>
        <button type="button" class="shrink-0 text-xs font-semibold text-brand" onClick={copyLink}>
          {copiedLink() ? props.copiedLabel : props.copyLabel}
        </button>
      </div>

      <div class="flex items-center gap-3">
        <QrPlaceholder />
        <span class="text-xs text-muted">{props.caption}</span>
      </div>

      <Show when={props.result.passphrase}>
        {(passphrase) => (
          <div class="notice-box">
            <div class="text-ink">{props.passphraseLabel}</div>
            <div class="mt-2 flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center">
              <span class="pill-badge w-full break-all sm:w-auto">{passphrase()}</span>
              <button
                class="btn btn-secondary w-full sm:w-auto"
                type="button"
                onClick={() => copyPassphrase(passphrase())}
              >
                {copiedPassphrase() ? props.copiedLabel : props.copyPassphraseLabel}
              </button>
            </div>
          </div>
        )}
      </Show>

      <Show when={props.result.incomplete}>
        <div class="notice-box">{props.partialLabel}</div>
      </Show>

      <button type="button" class="btn btn-secondary" onClick={props.onReset}>
        {props.anotherLabel}
      </button>
    </div>
  );
}
