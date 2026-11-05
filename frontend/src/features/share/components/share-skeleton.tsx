import { useI18n } from '../../../i18n/i18n.tsx';

/**
 * Shown while the fragment proof is being derived and the package is fetched,
 * so a combined link never flashes the gate on its way to the document.
 */
export function ShareSkeleton() {
  const { t } = useI18n();

  return (
    <div class="grid gap-4" aria-busy="true">
      <span class="sr-only">{t('share.loading')}</span>

      <div class="grid gap-2">
        <div class="h-7 w-48 animate-pulse rounded-md bg-panel-soft" />
        <div class="h-3 w-80 max-w-full animate-pulse rounded bg-panel-soft" />
      </div>

      <div class="h-44 animate-pulse rounded-xl border border-border-soft bg-panel" />

      <div class="grid gap-2">
        <div class="h-10 animate-pulse rounded-lg border border-border-soft bg-panel" />
        <div class="h-10 animate-pulse rounded-lg border border-border-soft bg-panel" />
      </div>
    </div>
  );
}
