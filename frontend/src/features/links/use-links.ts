import { createEffect, createMemo, createSignal } from 'solid-js';
import { useI18n } from '../../i18n/i18n.tsx';
import { deletePackage, listPackages, type PackageSummary } from '../../shared/api/content.ts';
import { getErrorMessage } from '../../shared/api/error-message.ts';
import { useAuth } from '../auth/auth-context.tsx';
import { isDead, linkStatus, matchesFilter, type LinkFilter, type LinkStatus } from './link-status.ts';

export function createLinks() {
  const { t } = useI18n();
  const auth = useAuth();

  const [packages, setPackages] = createSignal<PackageSummary[]>([]);
  const [loading, setLoading] = createSignal(false);
  const [loadingMore, setLoadingMore] = createSignal(false);
  const [error, setError] = createSignal<string | null>(null);
  const [loadMoreError, setLoadMoreError] = createSignal<string | null>(null);
  const [actionError, setActionError] = createSignal<string | null>(null);
  const [nextCursor, setNextCursor] = createSignal<string | null>(null);
  const [filter, setFilter] = createSignal<LinkFilter>('all');
  const [pendingDelete, setPendingDelete] = createSignal<PackageSummary | null>(null);

  let loadRequest = 0;

  const load = async () => {
    if (!auth.isAuthenticated()) {
      return;
    }

    const current = ++loadRequest;
    setLoading(true);
    setError(null);
    setLoadMoreError(null);
    setActionError(null);
    setNextCursor(null);

    try {
      const outcome = await listPackages();
      if (current !== loadRequest) {
        return;
      }

      if (outcome.data) {
        setPackages(outcome.data.items);
        setNextCursor(outcome.data.nextCursor);
      } else {
        setError(getErrorMessage(outcome.error, t('links.loadFailed')));
      }
    } catch (err) {
      if (current === loadRequest) {
        setError(getErrorMessage(err, t('links.loadFailed')));
      }
    } finally {
      if (current === loadRequest) {
        setLoading(false);
      }
    }
  };

  const reset = () => {
    loadRequest += 1;
    setPackages([]);
    setLoading(false);
    setLoadingMore(false);
    setError(null);
    setLoadMoreError(null);
    setActionError(null);
    setNextCursor(null);
    setFilter('all');
    setPendingDelete(null);
  };

  createEffect(
    () => auth.isAuthenticated(),
    (authenticated) => {
      if (authenticated) {
        void load();
      } else {
        reset();
      }
    }
  );

  const loadMore = async () => {
    const cursor = nextCursor();
    if (!cursor || loadingMore()) {
      return;
    }

    const generation = loadRequest;
    setLoadingMore(true);
    setLoadMoreError(null);

    try {
      const outcome = await listPackages(cursor);
      if (generation !== loadRequest) {
        return;
      }

      if (!outcome.data) {
        setLoadMoreError(getErrorMessage(outcome.error, t('links.loadMoreFailed')));
        return;
      }

      const page = outcome.data;
      setPackages((current) => {
        const loadedIds = new Set(current.map((item) => item.Id));
        return [...current, ...page.items.filter((item) => !loadedIds.has(item.Id))];
      });
      setNextCursor(page.nextCursor);
    } catch (err) {
      if (generation === loadRequest) {
        setLoadMoreError(getErrorMessage(err, t('links.loadMoreFailed')));
      }
    } finally {
      if (generation === loadRequest) {
        setLoadingMore(false);
      }
    }
  };

  const counts = createMemo<Record<LinkFilter, number>>(() => {
    const statuses = packages().map((item) => linkStatus(item));
    return {
      all: statuses.length,
      active: statuses.filter((status) => status === 'active').length,
      expiring: statuses.filter((status) => status === 'expiring').length,
      expired: statuses.filter(isDead).length
    };
  });

  const visiblePackages = createMemo(() => packages().filter((item) => matchesFilter(linkStatus(item), filter())));

  const requestDelete = (item: PackageSummary) => {
    setActionError(null);
    setPendingDelete(item);
  };

  const cancelDelete = () => {
    setPendingDelete(null);
  };

  const restore = (item: PackageSummary, index: number) => {
    setPackages((current) => {
      const next = [...current];
      next.splice(Math.min(index, next.length), 0, item);
      return next;
    });
  };

  const confirmDelete = async () => {
    const target = pendingDelete();
    if (!target) {
      return;
    }

    const index = packages().findIndex((item) => item.Id === target.Id);
    setPendingDelete(null);
    setActionError(null);
    setPackages((current) => current.filter((item) => item.Id !== target.Id));

    try {
      const outcome = await deletePackage(target.Id);
      // A 404 means cleanup already removed it; the row is gone either way.
      if (outcome.data || outcome.status === 404) {
        return;
      }

      restore(target, index);
      setActionError(getErrorMessage(outcome.error, t('links.delete.failed')));
    } catch (err) {
      restore(target, index);
      setActionError(getErrorMessage(err, t('links.delete.failed')));
    }
  };

  const login = () => {
    void auth.login();
  };

  const statusOf = (item: PackageSummary): LinkStatus => linkStatus(item);

  return {
    packages,
    visiblePackages,
    counts,
    loading,
    loadingMore,
    error,
    loadMoreError,
    actionError,
    nextCursor,
    loadMore,
    filter,
    setFilter,
    pendingDelete,
    requestDelete,
    cancelDelete,
    confirmDelete,
    load,
    login,
    isAuthenticated: auth.isAuthenticated,
    isAuthLoading: auth.isLoading,
    isAuthEnabled: auth.isEnabled,
    statusOf
  };
}
