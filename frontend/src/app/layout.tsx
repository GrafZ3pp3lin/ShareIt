import type { RouteSectionProps } from '@solidjs/router';
import { Show } from 'solid-js';
import { useAuth } from '../features/auth/auth-context.tsx';
import { useI18n } from '../i18n/i18n.tsx';
import { useTheme } from '../shared/theme/theme-context.tsx';
import IconLock from '~icons/lucide/lock';
import IconMoon from '~icons/lucide/moon';
import IconSun from '~icons/lucide/sun';

type AppLayoutProps = RouteSectionProps & {
  variant?: 'app' | 'share';
};

export function AppLayout(props: AppLayoutProps) {
  const auth = useAuth();
  const { t } = useI18n();
  const theme = useTheme();
  const isShare = () => props.variant === 'share';

  return (
    <div class="min-h-screen flex flex-col">
      <header class="border-b border-border-soft/80 bg-panel/90 sm:sticky sm:top-0 sm:z-50 sm:backdrop-blur-sm">
        <div class="mx-auto flex w-full max-w-275 flex-wrap items-center justify-between gap-x-6 gap-y-3 px-4 py-3 sm:py-4 sm:px-8">
          <div class="flex items-center gap-6">
            <a class="flex items-center gap-2 text-sm font-semibold text-ink" href="/">
              <img class="h-7 w-7 shrink-0" src="/favicon.svg" alt="" width="28" height="28" />
              <span>{t('app.brand')}</span>
            </a>
            <Show when={!isShare() && auth.isAuthenticated()}>
              <nav class="flex items-center gap-5">
                <a class="nav-tab" href="/">
                  {t('app.nav.upload')}
                </a>
                <a class="nav-tab" href="/links">
                  {t('app.nav.links')}
                </a>
              </nav>
            </Show>
          </div>
          <div class="flex items-center gap-3">
            <Show when={!isShare() && !auth.isAuthenticated() && auth.isEnabled}>
              <button
                type="button"
                class="text-sm text-muted transition hover:text-ink"
                onClick={() => void auth.login()}
              >
                {t('auth.signIn')}
              </button>
            </Show>
            <Show when={!isShare() && auth.isAuthenticated()}>
              <button type="button" class="text-sm text-muted transition hover:text-ink" onClick={auth.logout}>
                {t('app.nav.logout')}
              </button>
            </Show>
            <button
              type="button"
              class="pill-badge cursor-pointer"
              onClick={theme.toggle}
              aria-label={t('app.theme.toggleLabel')}
            >
              <Show when={theme.mode() === 'dark'} fallback={<IconSun class="h-3.5 w-3.5" />}>
                <IconMoon class="h-3.5 w-3.5" />
              </Show>
              <span>{theme.mode() === 'dark' ? t('app.theme.dark') : t('app.theme.light')}</span>
            </button>
          </div>
        </div>
      </header>
      <main class="mx-auto w-full max-w-275 flex-1 px-4 py-6 sm:px-8 sm:py-10">{props.children}</main>
      <footer class="mt-auto border-t border-border-soft/80 bg-panel/70">
        <div class="mx-auto flex w-full max-w-275 flex-wrap items-center justify-between gap-3 px-4 py-3.5 sm:px-8 sm:py-4">
          <div class="flex items-center gap-2.5">
            <span
              class="grid h-6 w-6 shrink-0 place-items-center rounded-md border border-border-soft bg-panel text-brand"
              aria-hidden="true"
            >
              <IconLock class="h-3.5 w-3.5" />
            </span>
            <p class="text-xs leading-relaxed text-ink">{t('app.footer')}</p>
          </div>
          <div class="flex items-center gap-4">
            <a class="inline-flex min-h-6 items-center text-xs text-muted transition hover:text-ink" href="/privacy">
              {t('privacy.link')}
            </a>
            <a class="inline-flex min-h-6 items-center text-xs text-muted transition hover:text-ink" href="/imprint">
              {t('imprint.link')}
            </a>
          </div>
        </div>
      </footer>
    </div>
  );
}
