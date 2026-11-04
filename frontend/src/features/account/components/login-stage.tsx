import { Show } from 'solid-js';
import IconLock from '~icons/lucide/lock';
import { useI18n } from '../../../i18n/i18n.tsx';

type LoginStageProps = {
  enabled: boolean;
  onLogin: () => void;
};

export function LoginStage(props: LoginStageProps) {
  const { t } = useI18n();

  return (
    <div class="mx-auto grid max-w-sm gap-4 py-4 text-center">
      <div class="mx-auto flex h-10 w-10 items-center justify-center rounded-full bg-panel-soft">
        <IconLock class="h-4 w-4 text-muted" />
      </div>
      <div class="grid gap-1">
        <h2 class="font-mono text-sm font-semibold text-ink">{t('account.login.heading')}</h2>
        <p class="font-sans text-xs text-muted">{t('account.login.body')}</p>
      </div>
      <Show when={props.enabled} fallback={<div class="caution font-sans">{t('auth.oidcNotConfiguredLong')}</div>}>
        <button type="button" class="btn btn-primary" onClick={props.onLogin}>
          {t('account.login.cta')}
        </button>
      </Show>
      <a class="font-sans text-xs text-muted transition hover:text-ink" href="/">
        {t('account.login.continueWithoutAccount')}
      </a>
    </div>
  );
}
