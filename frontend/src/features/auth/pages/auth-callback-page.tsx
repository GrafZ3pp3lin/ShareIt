import { useNavigate } from '@tanstack/solid-router';
import { createEffect } from 'solid-js';
import { useI18n } from '../../../i18n/i18n.tsx';
import { useAuth } from '../auth-context.tsx';

export function AuthCallbackPage() {
  const auth = useAuth();
  const navigate = useNavigate();
  const { t } = useI18n();

  createEffect(() => {
    if (!auth.isEnabled) {
      return;
    }
    const handle = async () => {
      await auth.handleCallback();
      void navigate({ to: '/' });
    };

    void handle();
  });

  return (
    <section class="mx-auto w-full max-w-2xl">
      <div class="surface-card">{auth.isEnabled ? t('auth.finishingSignIn') : t('auth.oidcNotConfigured')}</div>
    </section>
  );
}
