import { Show } from 'solid-js';
import { useI18n } from '../../../i18n';
import { NoticeBox } from '../../../shared/components/notice-box';
import { SurfaceCard } from '../../../shared/components/surface-card';
import { useAuth } from '../../auth/auth-context';

export function AccountPage() {
  const auth = useAuth();
  const { t } = useI18n();

  return (
    <SurfaceCard class="grid gap-3">
      <h2 class="text-lg font-semibold text-ink">{t('account.title')}</h2>
      <Show when={auth.isAuthenticated} fallback={<NoticeBox>{t('account.loginPrompt')}</NoticeBox>}>
        <p class="text-sm text-muted">{t('account.description')}</p>
        <NoticeBox>{t('account.notWired')}</NoticeBox>
      </Show>
    </SurfaceCard>
  );
}
