import { clsx } from 'clsx/lite';
import { Show } from 'solid-js';
import { useI18n } from '../../../i18n/i18n.tsx';
import type { PackageSummary } from '../../../shared/api/content.ts';
import { CopyButton } from '../../../shared/components/copy-button.tsx';
import { formatBytes } from '../../../shared/format-bytes.ts';
import { formatShortCode } from '../../../shared/short-code.ts';
import { loadShortCode } from '../../../shared/short-code-store.ts';
import { formatCreatedAt, packageLink, relativeSpan, shortPackageId, type RelativeSpan } from '../link-format.ts';
import { isDead, type LinkStatus } from '../link-status.ts';

type LinkRowProps = {
  item: PackageSummary;
  status: LinkStatus;
  onDelete: (item: PackageSummary) => void;
};

const STATUS_KEYS = {
  active: 'links.status.active',
  expiring: 'links.status.expiring',
  usedUp: 'links.status.usedUp',
  expired: 'links.status.expired'
} as const;

const dotClass = (status: LinkStatus) => {
  if (status === 'active') {
    return 'bg-brand';
  }
  return status === 'expiring' ? 'bg-ink' : 'bg-muted';
};

export function LinkRow(props: LinkRowProps) {
  const { t } = useI18n();

  const spanLabel = (span: RelativeSpan) => {
    if (span.unit === 'minute') {
      return t('links.minutes', { count: span.count });
    }
    if (span.unit === 'hour') {
      return t('links.hours', { count: span.count });
    }
    return t(span.count === 1 ? 'links.day' : 'links.days', { count: span.count });
  };

  const expiryLabel = () => {
    const span = relativeSpan(props.item.ExpiresAt);
    if (!span) {
      return null;
    }
    return t(span.past ? 'links.expiredAgo' : 'links.expiresIn', { value: spanLabel(span) });
  };

  const accessesLabel = () => {
    const remaining = props.item.RemainingAccesses;
    if (remaining == null) {
      return t('links.unlimited');
    }
    return t(remaining === 1 ? 'links.downloadLeft' : 'links.downloadsLeft', { count: remaining });
  };

  const statusNote = () => {
    if (props.status === 'usedUp') {
      return t('links.usedUpNote', {
        current: props.item.CurrentAccessCount,
        max: props.item.MaxAccessCount ?? props.item.CurrentAccessCount
      });
    }
    if (props.status === 'expired') {
      return expiryLabel() ?? t('links.status.expired');
    }

    return [expiryLabel(), accessesLabel()].filter((part) => part !== null).join(' · ');
  };

  // The full code is never available from the API: the server only stores the
  // three-character locator. The creating tab keeps it in sessionStorage so a
  // code-mode row can offer "copy code" instead of the unusable link.
  const storedCode = props.item.HasShortCode ? loadShortCode(props.item.Id) : null;
  const copyValue = () => (storedCode ? formatShortCode(storedCode) : packageLink(props.item.Id));
  const copyLabel = () => (storedCode ? t('links.actions.copyCode') : t('links.actions.copyLink'));

  return (
    <div class="grid gap-2.5 border-b border-border-soft px-4 py-3.5 last:border-b-0 min-[860px]:grid-cols-[minmax(0,1fr)_230px_200px] min-[860px]:items-center min-[860px]:gap-3.5">
      <div class="grid min-w-0 gap-0.5">
        <span class="truncate text-[13.5px] font-semibold text-ink">{formatCreatedAt(props.item.CreatedAt)}</span>
        <div class="flex flex-wrap items-center gap-x-1.5 gap-y-1 font-mono text-[10.5px] text-muted">
          <span title={props.item.Id}>{shortPackageId(props.item.Id)}</span>
          <Show when={props.item.HasShortCode}>
            <span aria-hidden="true">·</span>
            <span class="rounded border border-border-soft bg-panel-soft px-1 text-[9.5px] tracking-[0.06em] uppercase">
              {t('links.code')}
            </span>
          </Show>
          <span aria-hidden="true">·</span>
          <span>
            {t(props.item.ItemCount === 1 ? 'links.itemCount' : 'links.itemCountPlural', {
              count: props.item.ItemCount
            })}
          </span>
          <span aria-hidden="true">·</span>
          <span>{formatBytes(props.item.TotalSize)}</span>
          <span class="rounded border border-border-soft bg-panel-soft px-1 text-[9.5px] tracking-[0.06em] uppercase">
            {props.item.IsProtected ? t('links.protected') : t('links.unprotected')}
          </span>
        </div>
      </div>

      <div class="grid justify-items-start gap-0.5">
        <span class={clsx('pill-badge', isDead(props.status) && 'opacity-75')}>
          <span class={`h-1.5 w-1.5 rounded-full ${dotClass(props.status)}`} aria-hidden="true" />
          {t(STATUS_KEYS[props.status])}
        </span>
        <span class="font-mono text-[10.5px] leading-relaxed text-muted">{statusNote()}</span>
      </div>

      <div class="flex items-center gap-1 min-[860px]:justify-end">
        <CopyButton value={copyValue()} label={copyLabel()} variant="ghost" disabled={isDead(props.status)} />
        <button type="button" class="btn btn-ghost px-2.5 py-1 text-xs" onClick={() => props.onDelete(props.item)}>
          {t('links.actions.delete')}
        </button>
      </div>
    </div>
  );
}
