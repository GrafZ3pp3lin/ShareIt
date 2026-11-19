import { Show } from 'solid-js';
import { useI18n } from '../../../i18n/i18n.tsx';
import { CopyButton } from '../../../shared/components/copy-button.tsx';
import type { ComposerResult } from '../use-composer.ts';

type ResultPanelProps = {
  result: ComposerResult;
  onReset: () => void;
};

export function ResultPanel(props: ResultPanelProps) {
  const { t } = useI18n();

  const summary = () =>
    t('result.summary', {
      expiry: props.result.expiryLabel,
      downloads: props.result.accessesLabel,
      password: props.result.passwordLabel
    });

  return (
    <div class="mx-auto grid w-full max-w-[760px] gap-4">
      <div class="flex flex-wrap items-center justify-between gap-2">
        <span class="tiny-label text-ink">{t('result.ready')}</span>
        <span class="font-mono text-xs text-muted">{summary()}</span>
      </div>

      <div class="caution">
        <b class="text-ink">{t('result.selfOpenTitle')}</b> {t('result.selfOpenBody')}
      </div>

      <div class="grid gap-1.5">
        <div class="tiny-label">{t('result.link')}</div>
        <div class="link-field">
          <span class="min-w-0 flex-1 truncate">{props.result.link}</span>
          <CopyButton value={props.result.link} variant="inline" />
        </div>
      </div>

      <Show when={props.result.passphrase}>
        {(passphrase) => (
          <>
            <div class="grid gap-1.5">
              <div class="tiny-label">{t('result.passphrase')}</div>
              <div class="link-field">
                <span class="min-w-0 flex-1 truncate">{passphrase()}</span>
                <CopyButton value={passphrase()} variant="inline" />
              </div>
            </div>

            <div class="grid gap-1.5">
              <div class="tiny-label">{t('result.combined')}</div>
              <div class="link-field">
                <span class="min-w-0 flex-1 truncate">{props.result.linkWithPassphrase}</span>
                <CopyButton value={props.result.linkWithPassphrase ?? ''} variant="inline" />
              </div>
              <div class="caution">{t('result.channels')}</div>
            </div>
          </>
        )}
      </Show>

      <Show when={!props.result.passphrase}>
        <div class="caution">{t('result.noPasswordNote')}</div>
      </Show>

      <button type="button" class="btn btn-secondary w-fit" onClick={props.onReset}>
        {t('result.another')}
      </button>
    </div>
  );
}
