import { Show } from 'solid-js';
import type { ContentItemInPackage } from '../../../client';
import { Button } from '../../../shared/components/button';
import { NoticeBox } from '../../../shared/components/notice-box';
import { SurfaceCard } from '../../../shared/components/surface-card';
import { TextField } from '../../../shared/components/text-field';

type ShareItemCardProps = {
  item: ContentItemInPackage;
  title: string;
  encryptedNoticeLabel: string;
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
  passphraseValue: string;
  onPassphraseInput: (value: string) => void;
  onDecrypt: () => void;
  isDecrypting: boolean;
};

export function ShareItemCard(props: ShareItemCardProps) {
  return (
    <SurfaceCard class="grid gap-3">
      <div class="text-base font-semibold text-ink">{props.title}</div>

      <Show when={props.item.Text}>
        <Show
          when={props.isEncryptedText}
          fallback={
            <pre class="whitespace-pre-wrap rounded-xl border border-dashed border-border-soft bg-white px-3 py-3 text-sm text-muted">
              {props.rawText}
            </pre>
          }
        >
          <NoticeBox>{props.encryptedNoticeLabel}</NoticeBox>
          <div class="grid gap-3">
            <TextField
              type="password"
              placeholder={props.passphrasePlaceholder}
              value={props.passphraseValue}
              onInput={(event) => props.onPassphraseInput(event.currentTarget.value)}
            />
            <Button variant="secondary" type="button" onClick={props.onDecrypt} disabled={props.isDecrypting}>
              {props.isDecrypting ? props.decryptingLabel : props.decryptLabel}
            </Button>
            <Show when={props.decryptError}>
              <NoticeBox>{props.decryptError}</NoticeBox>
            </Show>
            <Show when={props.decryptedText}>
              <pre class="whitespace-pre-wrap rounded-xl border border-dashed border-border-soft bg-white px-3 py-3 text-sm text-muted">
                {props.decryptedText}
              </pre>
            </Show>
          </div>
        </Show>
      </Show>

      <Show when={!props.item.Text}>
        <div class="grid gap-3">
          <div class="text-sm text-muted">{props.mimeType}</div>
          <Show when={props.canDownload}>
            <Button variant="secondary" type="button" onClick={props.onDownload}>
              {props.downloadLabel}
            </Button>
          </Show>
          <Show when={props.downloadError}>
            <NoticeBox>{props.downloadError}</NoticeBox>
          </Show>
          <Show when={!props.canDownload}>
            <NoticeBox>{props.noDownloadTokenLabel}</NoticeBox>
          </Show>
        </div>
      </Show>
    </SurfaceCard>
  );
}
