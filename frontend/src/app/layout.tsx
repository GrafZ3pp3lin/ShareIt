import { Link, Outlet } from '@tanstack/solid-router';
import { Show } from 'solid-js';
import { useAuth } from '../features/auth/auth-context';
import { useI18n } from '../i18n';

export function AppLayout() {
  const auth = useAuth();
  const { t } = useI18n();

  return (
    <div class="min-h-screen flex flex-col">
      <header class="border-b border-border-soft/80 bg-white/90 sm:sticky sm:top-0 sm:z-50 sm:backdrop-blur-sm">
        <div class="mx-auto flex w-full max-w-5xl flex-col gap-4 px-4 py-4 sm:px-8 sm:py-5 lg:flex-row lg:items-center lg:justify-between">
          <Link class="flex items-center gap-3 text-sm font-semibold tracking-[0.2em] uppercase" to="/">
            <span
              class="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border-soft bg-white text-xs font-semibold"
              aria-hidden="true"
            >
              SI
            </span>
            <span>{t('app.brand')}</span>
          </Link>
          <nav class="flex w-full flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center lg:w-auto lg:justify-end">
            <Link class="btn btn-ghost w-full sm:w-auto" to="/">
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
                  <button class="btn btn-secondary w-full sm:w-auto" type="button" onClick={auth.login}>
                    {t('app.nav.login')}
                  </button>
                }
              >
                <Link class="btn btn-secondary w-full sm:w-auto" to="/account">
                  {t('app.nav.myUploads')}
                </Link>
                <button class="btn btn-ghost w-full sm:w-auto" type="button" onClick={auth.logout}>
                  {t('app.nav.logout')}
                </button>
              </Show>
            </Show>
          </nav>
        </div>
      </header>
      <main class="mx-auto w-full max-w-5xl flex-1 px-4 py-6 sm:px-8 sm:py-10">
        <Outlet />
      </main>
      <footer class="mx-auto w-full max-w-5xl px-4 pb-6 text-xs text-muted sm:px-8 sm:pb-8">{t('app.footer')}</footer>
    </div>
  );
}
