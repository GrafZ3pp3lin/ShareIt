import { useI18n } from '../../../i18n/i18n.tsx';

export function LinksSkeleton() {
  const { t } = useI18n();

  return (
    <div class="grid gap-4" aria-busy="true">
      <span class="sr-only">{t('links.loading')}</span>
      <div class="grid gap-2">
        <div class="h-7 w-40 animate-pulse rounded-md bg-panel-soft" />
        <div class="h-3 w-72 max-w-full animate-pulse rounded bg-panel-soft" />
      </div>
      <div class="h-3 w-56 animate-pulse rounded bg-panel-soft" />
      <div class="overflow-hidden rounded-xl border border-border-soft bg-panel">
        <div class="h-10 animate-pulse border-b border-border-soft bg-panel-soft" />
        <div class="h-16 animate-pulse border-b border-border-soft" />
        <div class="h-16 animate-pulse border-b border-border-soft" />
        <div class="h-16 animate-pulse" />
      </div>
    </div>
  );
}
