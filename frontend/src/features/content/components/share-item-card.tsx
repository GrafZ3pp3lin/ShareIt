import { Show } from 'solid-js';
import type { ContentItemInPackage } from '../../../client';

type ShareItemCardProps = {
  item: ContentItemInPackage;
  title: string;
  encryptedNoticeLabel: string;
  encryptedFileNoticeLabel: string;
  passphrasePlaceholder: string;
  decryptLabel: string;
  decryptingLabel: string;
  decryptError?: string;
  decryptedText?: string;
  rawText?: string;
  mimeType?: string | null;
  canDownload: boolean;
  downloadLabel: string;
  onDownload?: () => void;
  downloadError?: string;
  noDownloadTokenLabel: string;
  isEncryptedText: boolean;
  isEncryptedFile?: boolean;
  passphraseValue: string;
  onPassphraseInput: (value: string) => void;
  onDecrypt: () => void;
  isDecrypting: boolean;
};

export function ShareItemCard(props: ShareItemCardProps) {
  return (
    <div class="surface-card grid gap-3">
      <div class="text-base font-semibold text-ink">{props.title}</div>

      <Show when={props.item.Text}>
        <Show
          when={props.isEncryptedText}
          fallback={
            <pre class="overflow-x-auto whitespace-pre-wrap wrap-break-word rounded-xl border border-dashed border-border-soft bg-white px-3 py-3 text-sm text-muted">
              {props.rawText}
            </pre>
          }
        >
          <div class="notice-box">{props.encryptedNoticeLabel}</div>
          <div class="grid gap-3">
            <input
              class="text-field"
              type="password"
              placeholder={props.passphrasePlaceholder}
              value={props.passphraseValue}
              onInput={(event) => props.onPassphraseInput(event.currentTarget.value)}
            />
            <button class="btn btn-secondary" type="button" onClick={props.onDecrypt} disabled={props.isDecrypting}>
              {props.isDecrypting ? props.decryptingLabel : props.decryptLabel}
            </button>
            <Show when={props.decryptError}>
              <div class="notice-box">{props.decryptError}</div>
            </Show>
            <Show when={props.decryptedText}>
              <pre class="overflow-x-auto whitespace-pre-wrap wrap-break-word rounded-xl border border-dashed border-border-soft bg-white px-3 py-3 text-sm text-muted">
                {props.decryptedText}
              </pre>
            </Show>
          </div>
        </Show>
      </Show>

      <Show when={!props.item.Text}>
        <div class="grid gap-3">
          <Show when={props.isEncryptedFile} fallback={<div class="text-sm text-muted">{props.mimeType}</div>}>
            <div class="notice-box">{props.encryptedFileNoticeLabel}</div>
          </Show>
          <Show when={props.isEncryptedFile}>
            <input
              class="text-field"
              type="password"
              placeholder={props.passphrasePlaceholder}
              value={props.passphraseValue}
              onInput={(event) => props.onPassphraseInput(event.currentTarget.value)}
            />
          </Show>
          <Show when={props.canDownload}>
            <button class="btn btn-secondary" type="button" onClick={props.onDownload}>
              {props.downloadLabel}
            </button>
          </Show>
          <Show when={props.downloadError}>
            <div class="notice-box">{props.downloadError}</div>
          </Show>
          <Show when={!props.canDownload}>
            <div class="notice-box">{props.noDownloadTokenLabel}</div>
          </Show>
        </div>
      </Show>
    </div>
  );
}
