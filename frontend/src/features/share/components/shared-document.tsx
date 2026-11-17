import { For, Show, createMemo } from 'solid-js';
import type { ContentPackageResponse } from '../../../client/model/index.ts';
import { useI18n } from '../../../i18n/i18n.tsx';
import type { PackageKey } from '../../../shared/crypto/keys.ts';
import { formatBytes } from '../../../shared/format-bytes.ts';
import { FileRow } from './file-row.tsx';
import { TextBlock } from './text-block.tsx';
import { isTextItem, type ShareItem } from '../use-share.ts';

type SharedDocumentProps = {
  package: ContentPackageResponse;
  items: ShareItem[];
  packageId: string;
  packageKey: PackageKey | undefined;
  accessSessionToken: string | undefined;
  accessSessionExpiresAt: string;
  onAccessExpired: (failedToken?: string) => void;
  onPackageGone: () => void;
};

/**
 * The post-unlock page: text is the document, files are rows underneath.
 * `kind` only decides which of the two gets rendered.
 */
export function SharedDocument(props: SharedDocumentProps) {
  const { t } = useI18n();

  const textItems = createMemo(() => props.items.filter(isTextItem));
  const fileItems = createMemo(() => props.items.filter((item) => !isTextItem(item)));
  const totalSize = createMemo(() => props.items.reduce((sum, item) => sum + item.Size, 0));

  const countLabel = (
    count: number,
    singular: 'share.textCount' | 'share.fileCount',
    plural: 'share.textCountPlural' | 'share.fileCountPlural'
  ) => t(count === 1 ? singular : plural, { count });

  const expiryLabel = createMemo(() => {
    const expiresAt = props.package.ExpiresAt;
    if (!expiresAt) {
      return null;
    }

    const expires = Date.parse(expiresAt);
    if (Number.isNaN(expires)) {
      return null;
    }

    const minutes = Math.ceil((expires - Date.now()) / 60_000);
    if (minutes <= 0) {
      return t('share.expiresNow');
    }
    if (minutes < 60) {
      return t('share.expiresIn', { value: t('share.minutes', { count: minutes }) });
    }

    const hours = Math.ceil(minutes / 60);
    if (hours < 24) {
      return t('share.expiresIn', { value: t('share.hours', { count: hours }) });
    }

    const days = Math.ceil((expires - Date.now()) / 86_400_000);
    return t('share.expiresIn', { value: t(days === 1 ? 'share.day' : 'share.days', { count: days }) });
  });

  const accessesLabel = createMemo(() => {
    const remaining = props.package.RemainingAccesses;
    if (remaining == null) {
      return t('share.unlimitedDownloads');
    }

    const count = Number(remaining);
    return t(count === 1 ? 'share.downloadLeft' : 'share.downloadsLeft', { count });
  });

  const accessNote = createMemo(() => {
    const expires = Date.parse(props.accessSessionExpiresAt);
    if (Number.isNaN(expires)) {
      return '';
    }

    const time = new Intl.DateTimeFormat(undefined, { hour: '2-digit', minute: '2-digit' }).format(expires);
    return t('share.accessNote', { time });
  });

  const summary = createMemo(() => {
    const parts: string[] = [];

    if (textItems().length > 0) {
      parts.push(countLabel(textItems().length, 'share.textCount', 'share.textCountPlural'));
    }
    if (fileItems().length > 0) {
      parts.push(countLabel(fileItems().length, 'share.fileCount', 'share.fileCountPlural'));
    }
    if (totalSize() > 0) {
      parts.push(formatBytes(totalSize()));
    }
    const expiry = expiryLabel();
    if (expiry) {
      parts.push(expiry);
    }
    parts.push(accessesLabel());

    return parts.join(' · ');
  });

  return (
    <div class="grid gap-4">
      <div class="grid gap-1.5">
        <h1 class="text-2xl font-semibold tracking-tight text-ink">{t('share.title')}</h1>
        <p class="font-mono text-[11px] leading-relaxed text-muted">{summary()}</p>
      </div>

      <For each={textItems()}>
        {(item) => (
          <TextBlock
            packageId={props.packageId}
            item={item}
            packageKey={props.packageKey}
            accessSessionToken={props.accessSessionToken}
            onAccessExpired={props.onAccessExpired}
            onPackageGone={props.onPackageGone}
          />
        )}
      </For>

      <Show when={fileItems().length > 0}>
        <div class="grid gap-2">
          <span class="tiny-label">{t('share.filesLabel')}</span>
          <For each={fileItems()}>
            {(item) => (
              <FileRow
                packageId={props.packageId}
                item={item}
                packageKey={props.packageKey}
                accessSessionToken={props.accessSessionToken}
                onAccessExpired={props.onAccessExpired}
                onPackageGone={props.onPackageGone}
              />
            )}
          </For>
        </div>
      </Show>

      <p class="font-mono text-[10.5px] leading-relaxed text-muted">{accessNote()}</p>
    </div>
  );
}
