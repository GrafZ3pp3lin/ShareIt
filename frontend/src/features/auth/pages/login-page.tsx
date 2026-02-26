import { Show } from 'solid-js';
import { useI18n } from '../../../i18n';
import { Button } from '../../../shared/components/button';
import { NoticeBox } from '../../../shared/components/notice-box';
import { PillBadge } from '../../../shared/components/pill-badge';
import { SurfaceCard } from '../../../shared/components/surface-card';
import { useAuth } from '../auth-context';

export function LoginPage() {
  const auth = useAuth();
  const { t } = useI18n();

  return (
    <SurfaceCard class="grid gap-3">
      <h2 class="text-lg font-semibold text-ink">{t('auth.signIn')}</h2>
      <p class="text-sm text-muted">{t('auth.signInDescription')}</p>
      <Show when={auth.isEnabled} fallback={<NoticeBox>{t('auth.oidcNotConfiguredLong')}</NoticeBox>}>
        <Show when={!auth.isAuthenticated} fallback={<PillBadge>{t('auth.alreadySignedIn')}</PillBadge>}>
          <Button type="button" onClick={auth.login}>
            {t('auth.continueWithOidc')}
          </Button>
        </Show>
      </Show>
    </SurfaceCard>
  );
}
