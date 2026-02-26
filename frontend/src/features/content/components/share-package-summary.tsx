import { Show } from 'solid-js';
import { NoticeBox } from '../../../shared/components/notice-box';
import { PillBadge } from '../../../shared/components/pill-badge';
import { SurfaceCard } from '../../../shared/components/surface-card';

type SharePackageSummaryProps = {
  title: string;
  loadingLabel: string;
  loadError?: string;
  packageLabel?: string;
  itemsLabel?: string;
};

export function SharePackageSummary(props: SharePackageSummaryProps) {
  return (
    <SurfaceCard class="grid gap-3">
      <h2 class="text-lg font-semibold text-ink">{props.title}</h2>
      <Show when={props.loadingLabel}>
        <div>{props.loadingLabel}</div>
      </Show>
      <Show when={props.loadError}>
        <NoticeBox>{props.loadError}</NoticeBox>
      </Show>
      <Show when={props.packageLabel}>
        <div class="grid gap-3">
          <PillBadge>{props.packageLabel}</PillBadge>
          <Show when={props.itemsLabel}>
            <div class="text-sm text-muted">{props.itemsLabel}</div>
          </Show>
        </div>
      </Show>
    </SurfaceCard>
  );
}
