import { Show } from 'solid-js';
import IconLock from '~icons/lucide/lock';
import { useI18n } from '../../../i18n/i18n.tsx';

type LoginGateProps = {
  enabled: boolean;
  onLogin: () => void;
};

export function LoginGate(props: LoginGateProps) {
  const { t } = useI18n();

  return (
    <div class="grid place-items-center px-2 py-12 sm:py-16">
      <div class="grid w-full max-w-105 justify-items-center gap-3 rounded-2xl border border-border-soft bg-panel px-6 py-8 text-center shadow-button-soft">
        <div
          class="grid h-10 w-10 place-items-center rounded-full border border-border-soft bg-panel-soft text-muted"
          aria-hidden="true"
        >
          <IconLock class="h-4 w-4" />
        </div>
        <h1 class="text-xl font-semibold tracking-tight text-ink">{t('links.login.title')}</h1>
        <p class="max-w-[46ch] text-sm text-muted">{t('links.login.body')}</p>
        <Show
          when={props.enabled}
          fallback={<div class="caution w-full text-left">{t('auth.oidcNotConfiguredLong')}</div>}
        >
          <button type="button" class="btn btn-primary" onClick={props.onLogin}>
            {t('links.login.cta')}
          </button>
        </Show>
        <a class="font-mono text-[10.5px] text-muted underline transition hover:text-ink" href="/">
          {t('links.login.continueWithoutAccount')}
        </a>
      </div>
    </div>
  );
}
