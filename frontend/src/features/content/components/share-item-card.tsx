import { Show } from 'solid-js';
import type { ContentItemInPackage } from '../../../client/model/index.ts';
import { ENCRYPTED_FILE_MIME_TYPE } from '../../../shared/crypto/crypto.ts';
import { parseEncryptedTextEnvelope } from '../../../shared/crypto/envelope.ts';
import { useI18n } from '../../../i18n/i18n.tsx';

type ShareItemCardProps = {
  item: ContentItemInPackage;
  passphraseValue: string;
  onPassphraseInput: (value: string) => void;
  onDecrypt: () => void;
  isDecrypting?: boolean;
  /** Decrypted text once the passphrase proved correct (absent while locked). */
  decryptedText?: string;
  decryptError?: string;
  onDownload?: () => void;
  downloadError?: string;
};

export function ShareItemCard(props: ShareItemCardProps) {
  const { t } = useI18n();

  const isEncryptedText = () => Boolean(props.item.Text && parseEncryptedTextEnvelope(props.item.Text));
  const isEncryptedFile = () => props.item.MimeType === ENCRYPTED_FILE_MIME_TYPE;
  const canDownload = () => Boolean(props.item.DownloadToken);
  const title = () => props.item.FileName ?? (props.item.Type === 'text' ? t('share.textEntry') : t('share.content'));

  return (
    <div class="surface-card grid gap-3">
      <div class="text-base font-semibold text-ink">{title()}</div>

      <Show when={props.item.Text}>
        <Show
          when={isEncryptedText()}
          fallback={
            <pre class="overflow-x-auto whitespace-pre-wrap wrap-break-word rounded-xl border border-dashed border-border-soft bg-white px-3 py-3 text-sm text-muted">
              {props.item.Text}
            </pre>
          }
        >
          <div class="notice-box">{t('share.encryptedNotice')}</div>
          <div class="grid gap-3">
            <input
              class="text-field"
              type="password"
              placeholder={t('share.passphrasePlaceholder')}
              value={props.passphraseValue}
              onInput={(event) => props.onPassphraseInput(event.currentTarget.value)}
            />
            <button class="btn btn-secondary" type="button" onClick={props.onDecrypt} disabled={props.isDecrypting}>
              {props.isDecrypting ? t('share.decrypting') : t('share.decrypt')}
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
          <Show when={isEncryptedFile()} fallback={<div class="text-sm text-muted">{props.item.MimeType}</div>}>
            <div class="notice-box">{t('share.encryptedFileNotice')}</div>
          </Show>
          <Show when={isEncryptedFile()}>
            <input
              class="text-field"
              type="password"
              placeholder={t('share.passphrasePlaceholder')}
              value={props.passphraseValue}
              onInput={(event) => props.onPassphraseInput(event.currentTarget.value)}
            />
          </Show>
          <Show when={canDownload()}>
            <button class="btn btn-secondary" type="button" onClick={props.onDownload}>
              {t('share.downloadFile')}
            </button>
          </Show>
          <Show when={props.downloadError}>
            <div class="notice-box">{props.downloadError}</div>
          </Show>
          <Show when={!canDownload()}>
            <div class="notice-box">{t('share.noDownloadToken')}</div>
          </Show>
        </div>
      </Show>
    </div>
  );
}
