import type { RouteSectionProps } from '@solidjs/router';
import { Show } from 'solid-js';
import { useAuth } from '../features/auth/auth-context.tsx';
import { useI18n } from '../i18n/i18n.tsx';
import { useTheme } from '../shared/theme/theme-context.tsx';
import IconMoon from '~icons/lucide/moon';
import IconSun from '~icons/lucide/sun';

export function AppLayout(props: RouteSectionProps) {
  const auth = useAuth();
  const { t } = useI18n();
  const theme = useTheme();

  return (
    <div class="min-h-screen flex flex-col">
      <header class="border-b border-border-soft/80 bg-panel/90 sm:sticky sm:top-0 sm:z-50 sm:backdrop-blur-sm">
        <div class="mx-auto flex w-full max-w-5xl flex-col gap-4 px-4 py-4 sm:flex-row sm:items-center sm:justify-between sm:px-8">
          <div class="flex items-center gap-6">
            <a class="flex items-center gap-3 text-sm font-semibold tracking-[0.2em] uppercase" href="/">
              <span
                class="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border-soft bg-panel text-xs font-semibold"
                aria-hidden="true"
              >
                SI
              </span>
              <span>{t('app.brand')}</span>
            </a>
            <nav class="flex items-center gap-5">
              <a class="nav-tab" href="/">
                {t('app.nav.upload')}
              </a>
              <a class="nav-tab" href="/account">
                {t('app.nav.myFiles')}
              </a>
            </nav>
          </div>
          <div class="flex items-center gap-3">
            <Show when={auth.isAuthenticated()}>
              <button
                type="button"
                class="font-mono text-sm text-muted transition hover:text-ink"
                onClick={auth.logout}
              >
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
      <main class="mx-auto w-full max-w-5xl flex-1 px-4 py-6 sm:px-8 sm:py-10">{props.children}</main>
      <footer class="mx-auto w-full max-w-5xl px-4 pb-6 text-xs text-muted sm:px-8 sm:pb-8">{t('app.footer')}</footer>
    </div>
  );
}
