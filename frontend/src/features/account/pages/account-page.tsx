import { Show } from 'solid-js';
import { useI18n } from '../../../i18n';
import { useAuth } from '../../auth/auth-context';

export function AccountPage() {
  const auth = useAuth();
  const { t } = useI18n();

  return (
    <div class="surface-card grid gap-3">
      <h2 class="text-lg font-semibold text-ink">{t('account.title')}</h2>
      <Show when={auth.isAuthenticated} fallback={<div class="notice-box">{t('account.loginPrompt')}</div>}>
        <p class="text-sm text-muted">{t('account.description')}</p>
        <div class="notice-box">{t('account.notWired')}</div>
      </Show>
    </div>
  );
}
