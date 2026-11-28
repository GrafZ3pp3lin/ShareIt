import { Show } from 'solid-js';
import IconLock from '~icons/lucide/lock';
import { useI18n } from '../../../i18n/i18n.tsx';
import type { GateMessage } from '../use-share.ts';

type GateProps = {
  gone: boolean;
  passphrase: string;
  message: GateMessage | null;
  onPassphraseInput: (value: string) => void;
  onSubmit: () => void;
};

/**
 * The only thing the page renders until the package is unlocked. It contains
 * no package data by design: no id, count, names, expiry or counters.
 */
export function Gate(props: GateProps) {
  const { t } = useI18n();

  return (
    <div class="grid place-items-center px-2 py-10 sm:py-16">
      <form
        class="gate"
        onSubmit={(event) => {
          event.preventDefault();
          props.onSubmit();
        }}
      >
        <div
          class="grid h-10 w-10 place-items-center rounded-full border border-border-soft bg-panel-soft text-muted"
          aria-hidden="true"
        >
          <IconLock class="h-4 w-4" />
        </div>

        <h1 class="text-xl font-semibold tracking-tight text-ink">
          {props.gone ? t('gate.goneTitle') : t('gate.title')}
        </h1>

        <Show
          when={!props.gone}
          fallback={
            <>
              <p class="text-sm text-muted">{t('gate.goneBody')}</p>
              <a class="btn btn-secondary w-fit" href="/">
                {t('gate.home')}
              </a>
            </>
          }
        >
          <p class="text-sm text-muted">{t('gate.subtitle')}</p>

          <input
            class="text-field font-mono"
            type="password"
            autofocus
            autocomplete="off"
            spellcheck={false}
            aria-label={t('gate.passphraseLabel')}
            placeholder={t('gate.passphrasePlaceholder')}
            value={props.passphrase}
            onInput={(event) => props.onPassphraseInput(event.currentTarget.value)}
          />

          <Show when={props.message}>
            {(message) => (
              <div class="caution" role="alert">
                <b class="text-ink">{message().title}</b> {message().body}
              </div>
            )}
          </Show>

          <button class="btn btn-primary" type="submit">
            {props.message ? t('gate.tryAgain') : t('gate.unlock')}
          </button>

          <p class="note">{t('gate.note')}</p>
        </Show>
      </form>
    </div>
  );
}
