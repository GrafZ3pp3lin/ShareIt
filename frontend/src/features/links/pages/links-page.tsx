import { For, Show } from 'solid-js';
import IconPlus from '~icons/lucide/plus';
import { useI18n } from '../../../i18n/i18n.tsx';
import { ConfirmDialog } from '../../../shared/components/confirm-dialog.tsx';
import { EmptyState } from '../components/empty-state.tsx';
import { LinkFilters } from '../components/link-filters.tsx';
import { LinkRow } from '../components/link-row.tsx';
import { LinksSkeleton } from '../components/links-skeleton.tsx';
import { LoginGate } from '../components/login-gate.tsx';
import { formatCreatedAt } from '../link-format.ts';
import { createLinks } from '../use-links.ts';

type LoadErrorProps = {
  message: string;
  onRetry: () => void;
};

function LoadError(props: LoadErrorProps) {
  const { t } = useI18n();

  return (
    <div class="grid place-items-center px-2 py-12 sm:py-16">
      <div class="grid w-full max-w-105 justify-items-center gap-3 rounded-2xl border border-border-soft bg-panel px-6 py-8 text-center shadow-button-soft">
        <h1 class="text-xl font-semibold tracking-tight text-ink">{t('links.loadFailedTitle')}</h1>
        <div class="caution w-full text-left" role="alert">
          {props.message}
        </div>
        <button type="button" class="btn btn-secondary" onClick={props.onRetry}>
          {t('links.retry')}
        </button>
      </div>
    </div>
  );
}

export function LinksPage() {
  const { t } = useI18n();
  const links = createLinks();

  const deleteTitle = () => {
    const item = links.pendingDelete();
    return item ? t('links.delete.title', { name: formatCreatedAt(item.CreatedAt) }) : '';
  };

  const deleteBody = () => {
    const item = links.pendingDelete();
    if (!item) {
      return '';
    }
    return t(item.ItemCount === 1 ? 'links.delete.bodyOne' : 'links.delete.bodyMany', {
      count: item.ItemCount
    });
  };

  return (
    <Show
      when={links.isAuthenticated()}
      fallback={
        <Show when={links.isAuthLoading()} fallback={<LoginGate enabled={links.isAuthEnabled} onLogin={links.login} />}>
          <LinksSkeleton />
        </Show>
      }
    >
      <Show
        when={links.loading() && links.packages().length === 0}
        fallback={
          <Show
            when={links.packages().length > 0}
            fallback={
              <Show when={links.error()} fallback={<EmptyState />}>
                {(message) => <LoadError message={message()} onRetry={() => void links.load()} />}
              </Show>
            }
          >
            <div class="grid gap-4">
              <div class="flex flex-wrap items-end justify-between gap-3">
                <div class="grid gap-1.5">
                  <h1 class="text-2xl font-semibold tracking-tight text-ink">{t('links.title')}</h1>
                  <p class="text-sm text-muted">{t('links.subline')}</p>
                </div>
                <a class="btn btn-primary" href="/">
                  <IconPlus class="h-4 w-4" />
                  {t('links.new')}
                </a>
              </div>

              <Show when={links.error()}>
                {(message) => (
                  <div class="caution" role="alert">
                    {message()}
                  </div>
                )}
              </Show>

              <Show when={links.actionError()}>
                {(message) => (
                  <div class="caution" role="alert">
                    {message()}
                  </div>
                )}
              </Show>

              <LinkFilters active={links.filter()} counts={links.counts()} onChange={links.setFilter} />

              <div class="overflow-hidden rounded-xl border border-border-soft bg-panel shadow-button-soft">
                <div class="hidden grid-cols-[minmax(0,1fr)_230px_200px] gap-3.5 border-b border-border-soft bg-panel-soft px-4 py-2.5 min-[860px]:grid">
                  <span class="tiny-label">{t('links.table.created')}</span>
                  <span class="tiny-label">{t('links.table.status')}</span>
                  <span />
                </div>

                <Show
                  when={links.visiblePackages().length > 0}
                  fallback={
                    <div class="px-4 py-8 text-center font-mono text-[11px] text-muted">{t('links.filterEmpty')}</div>
                  }
                >
                  <For each={links.visiblePackages()}>
                    {(item) => <LinkRow item={item} status={links.statusOf(item)} onDelete={links.requestDelete} />}
                  </For>
                </Show>

                <Show when={links.nextCursor() !== null || links.loadMoreError()}>
                  <div class="grid justify-items-center gap-2 border-t border-border-soft p-3">
                    <Show when={links.loadMoreError()}>
                      {(message) => (
                        <div class="caution w-full text-center" role="alert">
                          {message()}
                        </div>
                      )}
                    </Show>
                    <Show when={links.nextCursor() !== null}>
                      <button
                        type="button"
                        class="btn btn-secondary"
                        disabled={links.loadingMore()}
                        onClick={() => void links.loadMore()}
                      >
                        {links.loadingMore()
                          ? t('links.loadingMore')
                          : links.loadMoreError()
                            ? t('links.retry')
                            : t('links.loadMore')}
                      </button>
                    </Show>
                  </div>
                </Show>
              </div>

              <p class="font-mono text-[10.5px] leading-relaxed text-muted">{t('links.copyNote')}</p>
            </div>
          </Show>
        }
      >
        <LinksSkeleton />
      </Show>

      <ConfirmDialog
        open={links.pendingDelete() !== null}
        title={deleteTitle()}
        body={deleteBody()}
        confirmLabel={t('links.delete.confirm')}
        cancelLabel={t('links.delete.cancel')}
        onConfirm={() => void links.confirmDelete()}
        onCancel={links.cancelDelete}
      />
    </Show>
  );
}
