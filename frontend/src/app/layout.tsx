import { Link, Outlet } from '@tanstack/solid-router';
import { Show } from 'solid-js';
import { useAuth } from '../features/auth/auth-context';
import { useI18n } from '../i18n';
import { getButtonClass } from '../shared/components/button';
import { PillBadge } from '../shared/components/pill-badge';

export function AppLayout() {
  const auth = useAuth();
  const { t } = useI18n();

  const buttonSecondary = getButtonClass('secondary');
  const buttonGhost = getButtonClass('ghost');

  return (
    <div class="min-h-screen flex flex-col">
      <header class="flex flex-wrap items-center justify-between gap-4 px-5 py-6 sm:px-8 lg:px-14">
        <Link class="flex items-center gap-3 font-bold tracking-wide" to="/">
          <span
            class="h-10 w-10 rounded-2xl bg-gradient-to-br from-brand to-brand-deep shadow-[inset_0_0_0_2px_rgba(255,255,255,0.2)]"
            aria-hidden="true"
          />
          <span>{t('app.brand')}</span>
        </Link>
        <nav class="flex flex-wrap items-center gap-3">
          <Link class={buttonGhost} to="/">
            {t('app.nav.upload')}
          </Link>
          <Show
            when={auth.isEnabled}
            fallback={
              <PillBadge as="span" title={t('app.nav.oidcNotConfigured')}>
                {t('app.nav.loginUnavailable')}
              </PillBadge>
            }
          >
            <Show
              when={auth.isAuthenticated}
              fallback={
                <Link class={buttonSecondary} to="/login">
                  {t('app.nav.login')}
                </Link>
              }
            >
              <Link class={buttonSecondary} to="/account">
                {t('app.nav.myUploads')}
              </Link>
              <button class={buttonGhost} onClick={auth.logout}>
                {t('app.nav.logout')}
              </button>
            </Show>
          </Show>
        </nav>
      </header>
      <main class="flex-1 px-5 pb-12 sm:px-8 lg:px-14">
        <Outlet />
      </main>
      <footer class="px-5 pb-8 text-sm text-muted sm:px-8 lg:px-14">{t('app.footer')}</footer>
    </div>
  );
}
