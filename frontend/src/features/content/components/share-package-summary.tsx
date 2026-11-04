import { Show } from 'solid-js';
import type { ContentPackageDetailsResponse } from '../../../client/model/index.ts';
import { useI18n } from '../../../i18n/i18n.tsx';

type SharePackageSummaryProps = {
  loading: boolean;
  loadError?: string;
  details?: ContentPackageDetailsResponse | null;
};

export function SharePackageSummary(props: SharePackageSummaryProps) {
  const { t } = useI18n();

  return (
    <div class="surface-card grid gap-3">
      <h2 class="text-lg font-semibold text-ink">{t('share.title')}</h2>
      <Show when={props.loading}>
        <div>{t('share.loading')}</div>
      </Show>
      <Show when={props.loadError}>
        <div class="caution">{props.loadError}</div>
      </Show>
      <Show when={props.details}>
        {(details) => (
          <div class="grid gap-3">
            <div class="pill-badge">{t('share.packageLabel', { id: details().Package.Id })}</div>
            <div class="text-sm text-muted">{t('share.itemsLabel', { count: details().Package.ItemCount })}</div>
          </div>
        )}
      </Show>
    </div>
  );
}
