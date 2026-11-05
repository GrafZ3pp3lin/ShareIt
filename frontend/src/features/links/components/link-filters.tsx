import { clsx } from 'clsx/lite';
import { For, createMemo } from 'solid-js';
import { useI18n } from '../../../i18n/i18n.tsx';
import type { LinkFilter } from '../link-status.ts';

type LinkFiltersProps = {
  active: LinkFilter;
  counts: Record<LinkFilter, number>;
  onChange: (filter: LinkFilter) => void;
};

const FILTERS: LinkFilter[] = ['all', 'active', 'expiring', 'expired'];

const LABEL_KEYS = {
  all: 'links.filters.all',
  active: 'links.filters.active',
  expiring: 'links.filters.expiring',
  expired: 'links.filters.expired'
} as const;

export function LinkFilters(props: LinkFiltersProps) {
  const { t } = useI18n();

  const items = createMemo(() =>
    FILTERS.map((filter) => ({
      filter,
      label: t(LABEL_KEYS[filter]),
      count: props.counts[filter]
    }))
  );

  return (
    <div class="flex flex-wrap items-center gap-4 px-0.5 font-mono text-[11.5px] text-muted">
      <For each={items()}>
        {(item) => (
          <button
            type="button"
            class={clsx(
              'cursor-pointer border-b pb-0.5 transition',
              props.active === item.filter ? 'border-ink text-ink' : 'border-transparent hover:text-ink'
            )}
            aria-pressed={props.active === item.filter ? 'true' : 'false'}
            onClick={() => props.onChange(item.filter)}
          >
            {item.label} <span class="opacity-70">{item.count}</span>
          </button>
        )}
      </For>
    </div>
  );
}
