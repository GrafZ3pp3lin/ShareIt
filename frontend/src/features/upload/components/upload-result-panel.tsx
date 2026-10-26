import { Show, createSignal } from 'solid-js';
import IconCheck from '~icons/lucide/check';
import { QrPlaceholder } from './qr-placeholder.tsx';
import type { UploadResult } from './upload-types.ts';
import { useI18n } from '../../../i18n/i18n.tsx';

type UploadResultPanelProps = {
  result: UploadResult;
  onReset: () => void;
};

export function UploadResultPanel(props: UploadResultPanelProps) {
  const { t } = useI18n();
  const [copiedLink, setCopiedLink] = createSignal(false);
  const [copiedPassphrase, setCopiedPassphrase] = createSignal(false);

  const copyToClipboard = async (value: string) => {
    try {
      await navigator.clipboard.writeText(value);
      return true;
    } catch {
      return false;
    }
  };

  const copyLink = async () => {
    if (!(await copyToClipboard(props.result.link))) {
      return;
    }
    setCopiedLink(true);
    setTimeout(() => setCopiedLink(false), 1500);
  };

  const copyPassphrase = async (passphrase: string) => {
    if (!(await copyToClipboard(passphrase))) {
      return;
    }
    setCopiedPassphrase(true);
    setTimeout(() => setCopiedPassphrase(false), 1500);
  };

  return (
    <div class="grid gap-4 font-mono">
      <div class="flex items-center gap-2 text-xs text-brand-deep">
        <IconCheck class="h-3.5 w-3.5" />
        <span>{t('upload.result.heading')}</span>
      </div>

      <div class="flex items-center gap-3 rounded-lg border border-border-soft bg-panel-soft/45 px-3 py-2.5">
        <span class="flex-1 overflow-hidden text-sm text-ink text-ellipsis whitespace-nowrap">{props.result.link}</span>
        <button type="button" class="shrink-0 text-xs font-semibold text-brand" onClick={() => void copyLink()}>
          {copiedLink() ? t('upload.result.copied') : t('upload.result.copy')}
        </button>
      </div>

      <div class="flex items-center gap-3">
        <QrPlaceholder />
        <span class="text-xs text-muted">
          {t(props.result.encrypted ? 'upload.result.captionWithPassword' : 'upload.result.captionPlain', {
            expiry: props.result.expiryLabel
          })}
        </span>
      </div>

      <Show when={props.result.passphrase}>
        {(passphrase) => (
          <div class="notice-box">
            <div class="text-ink">{t('upload.passphraseLabel')}</div>
            <div class="mt-2 flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center">
              <span class="pill-badge w-full break-all sm:w-auto">{passphrase()}</span>
              <button
                class="btn btn-secondary w-full sm:w-auto"
                type="button"
                onClick={() => void copyPassphrase(passphrase())}
              >
                {copiedPassphrase() ? t('upload.result.copied') : t('upload.copyPassphrase')}
              </button>
            </div>
          </div>
        )}
      </Show>

      <Show when={props.result.incomplete}>
        <div class="notice-box">{t('upload.result.partialNotice')}</div>
      </Show>

      <button type="button" class="btn btn-secondary" onClick={props.onReset}>
        {t('upload.result.another')}
      </button>
    </div>
  );
}
