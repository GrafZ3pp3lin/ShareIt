import { Show } from 'solid-js';

type SharePackageSummaryProps = {
  title: string;
  loadingLabel: string;
  loadError?: string;
  packageLabel?: string;
  itemsLabel?: string;
};

export function SharePackageSummary(props: SharePackageSummaryProps) {
  return (
    <div class="surface-card grid gap-3">
      <h2 class="text-lg font-semibold text-ink">{props.title}</h2>
      <Show when={props.loadingLabel}>
        <div>{props.loadingLabel}</div>
      </Show>
      <Show when={props.loadError}>
        <div class="notice-box">{props.loadError}</div>
      </Show>
      <Show when={props.packageLabel}>
        <div class="grid gap-3">
          <div class="pill-badge">{props.packageLabel}</div>
          <Show when={props.itemsLabel}>
            <div class="text-sm text-muted">{props.itemsLabel}</div>
          </Show>
        </div>
      </Show>
    </div>
  );
}
