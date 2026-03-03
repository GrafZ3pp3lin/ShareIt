import { Link, Outlet } from '@tanstack/solid-router';
import { Show } from 'solid-js';
import { useAuth } from '../features/auth/auth-context';
import { useI18n } from '../i18n';

export function AppLayout() {
  const auth = useAuth();
  const { t } = useI18n();

  return (
    <div class="min-h-screen flex flex-col">
      <header class="fixed inset-x-0 top-0 z-50 border-b border-border-soft/80 bg-white/80 backdrop-blur-sm">
        <div class="mx-auto flex w-full max-w-5xl flex-wrap items-center justify-between gap-4 px-5 py-5 sm:px-8">
          <Link class="flex items-center gap-3 text-sm font-semibold tracking-[0.2em] uppercase" to="/">
            <span
              class="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border-soft bg-white text-xs font-semibold"
              aria-hidden="true"
            >
              SI
            </span>
            <span>{t('app.brand')}</span>
          </Link>
          <nav class="flex flex-wrap items-center gap-2">
            <Link class="btn btn-ghost" to="/">
              {t('app.nav.upload')}
            </Link>
            <Show
              when={auth.isEnabled}
              fallback={
                <span class="pill-badge" title={t('app.nav.oidcNotConfigured')}>
                  {t('app.nav.loginUnavailable')}
                </span>
              }
            >
              <Show
                when={auth.isAuthenticated}
                fallback={
                  <button class="btn btn-secondary" type="button" onClick={auth.login}>
                    {t('app.nav.login')}
                  </button>
                }
              >
                <Link class="btn btn-secondary" to="/account">
                  {t('app.nav.myUploads')}
                </Link>
                <button class="btn btn-ghost" type="button" onClick={auth.logout}>
                  {t('app.nav.logout')}
                </button>
              </Show>
            </Show>
          </nav>
        </div>
      </header>
      <main class="mx-auto w-full max-w-5xl flex-1 px-5 pt-28 pb-8 sm:px-8 sm:pt-30 sm:pb-10">
        <Outlet />
      </main>
      <footer class="mx-auto w-full max-w-5xl px-5 pb-8 text-xs text-muted sm:px-8">{t('app.footer')}</footer>
    </div>
  );
}
