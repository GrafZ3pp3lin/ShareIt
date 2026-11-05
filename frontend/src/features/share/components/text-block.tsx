import { Show, createSignal, onSettled } from 'solid-js';
import { useI18n } from '../../../i18n/i18n.tsx';
import { getErrorMessage } from '../../../shared/api/error-message.ts';
import { CopyButton } from '../../../shared/components/copy-button.tsx';
import type { PackageKey } from '../../../shared/crypto/keys.ts';
import { readShareItem } from '../read-item.ts';
import type { ShareItem } from '../use-share.ts';

type TextBlockProps = {
  packageId: string;
  item: ShareItem;
  packageKey: PackageKey | undefined;
};

const decoder = new TextDecoder();

/**
 * Renders one text item. Each block loads independently, so a large file or a
 * slow download never holds up the document text.
 */
export function TextBlock(props: TextBlockProps) {
  const { t } = useI18n();
  const [state, setState] = createSignal<'loading' | 'ready' | 'error'>('loading');
  const [text, setText] = createSignal('');
  const [error, setError] = createSignal('');

  let request = 0;

  const load = async () => {
    const current = ++request;
    setState('loading');
    setError('');

    try {
      const bytes = await readShareItem(props.packageId, props.item, props.packageKey);
      if (current !== request) {
        return;
      }
      setText(decoder.decode(bytes));
      setState('ready');
    } catch (err) {
      if (current !== request) {
        return;
      }
      setError(getErrorMessage(err, t('share.textErrorBody')));
      setState('error');
    }
  };

  onSettled(() => {
    void load();
  });

  return (
    <div class="grid gap-3 rounded-xl border border-border-soft bg-panel px-5 py-4 shadow-button-soft">
      <div class="flex items-center justify-between gap-3">
        <span class="tiny-label">{t('share.textLabel')}</span>
        <Show when={state() === 'ready'}>
          <CopyButton value={text()} variant="inline" />
        </Show>
      </div>

      <Show when={state() === 'loading'}>
        <div class="grid gap-2" aria-busy="true">
          <span class="sr-only">{t('share.textLoading')}</span>
          <div class="h-3 w-11/12 animate-pulse rounded bg-panel-soft" />
          <div class="h-3 w-4/5 animate-pulse rounded bg-panel-soft" />
          <div class="h-3 w-2/3 animate-pulse rounded bg-panel-soft" />
        </div>
      </Show>

      <Show when={state() === 'error'}>
        <div class="grid gap-2">
          <div class="caution" role="alert">
            <b class="text-ink">{t('share.textErrorTitle')}</b> {error()}
          </div>
          <button type="button" class="btn btn-secondary w-fit" onClick={() => void load()}>
            {t('share.retry')}
          </button>
        </div>
      </Show>

      <Show when={state() === 'ready'}>
        <div class="max-w-[68ch] text-[15px] leading-relaxed whitespace-pre-wrap break-words text-ink">{text()}</div>
      </Show>
    </div>
  );
}
