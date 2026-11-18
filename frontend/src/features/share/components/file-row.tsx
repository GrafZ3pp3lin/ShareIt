import { Show, createSignal } from 'solid-js';
import { useI18n } from '../../../i18n/i18n.tsx';
import { getErrorMessage } from '../../../shared/api/error-message.ts';
import type { PackageKey } from '../../../shared/crypto/keys.ts';
import { formatBytes } from '../../../shared/format-bytes.ts';
import { fetchShareItem, readShareItem } from '../read-item.ts';
import { saveBlob, saveResponseStream } from '../save-file.ts';
import type { ShareItem } from '../use-share.ts';

type FileRowProps = {
  packageId: string;
  item: ShareItem;
  packageKey: PackageKey | undefined;
  accessSessionToken: string | undefined;
  onAccessExpired?: (failedToken?: string) => void;
  onPackageGone?: () => void;
};

/**
 * One file row. The content is fetched lazily on click through the package's
 * access session, so reading the page does not spend every item's download.
 * Errors stay with the row and can be retried without re-unlocking the package.
 */
export function FileRow(props: FileRowProps) {
  const { t } = useI18n();
  const [state, setState] = createSignal<'idle' | 'loading' | 'error'>('idle');
  const [error, setError] = createSignal('');

  let request = 0;

  const download = async () => {
    const current = ++request;
    setState('loading');
    setError('');

    try {
      const token = props.accessSessionToken;

      // An unencrypted item that is not inlined can be written straight to
      // disk. Encrypted bytes have to be buffered until the envelope is opened.
      if (!props.packageKey && props.item.Payload == null && token) {
        const streamed = await saveResponseStream(props.item.displayName, () =>
          fetchShareItem(props.packageId, props.item, token, props.onAccessExpired, props.onPackageGone)
        );

        if (streamed !== 'unsupported') {
          if (current === request) {
            setState('idle');
          }
          return;
        }
      }

      const bytes = await readShareItem(
        props.packageId,
        props.item,
        props.packageKey,
        props.accessSessionToken,
        props.onAccessExpired,
        props.onPackageGone
      );
      if (current !== request) {
        return;
      }
      saveBlob(bytes, props.item.displayName, props.item.mime);
      setState('idle');
    } catch (err) {
      if (current !== request) {
        return;
      }
      setError(getErrorMessage(err, t('share.downloadFailedBody')));
      setState('error');
    }
  };

  return (
    <div class="grid gap-1.5">
      <div class="file-row">
        <span class="min-w-0 flex-1 truncate" title={props.item.displayName}>
          {props.item.displayName}
        </span>
        <span class="shrink-0 text-[10.5px] text-muted">{formatBytes(props.item.Size)}</span>
        <button
          type="button"
          class="btn btn-secondary shrink-0 px-2.5 py-1 text-xs"
          disabled={state() === 'loading'}
          onClick={() => void download()}
        >
          {state() === 'loading'
            ? t('share.downloading')
            : state() === 'error'
              ? t('share.retry')
              : t('share.download')}
        </button>
      </div>

      <Show when={state() === 'error'}>
        <div class="caution" role="alert">
          <b class="text-ink">{t('share.downloadFailedTitle')}</b> {error()}
        </div>
      </Show>
    </div>
  );
}
