import { For } from 'solid-js';
import { useI18n } from '../../../i18n/i18n.tsx';
import { formatBytes } from './format-bytes.ts';

type ComposerFilesProps = {
  files: File[];
  totalSize: number;
  limitLabel?: string;
  onRemove: (index: number) => void;
};

export function ComposerFiles(props: ComposerFilesProps) {
  const { t } = useI18n();

  return (
    <div class="flex min-h-0 flex-col border-t border-border-soft pt-3 sm:border-t-0 sm:border-l sm:pl-4 sm:pt-0">
      <div class="mb-1.5 flex items-baseline justify-between gap-2 font-mono text-[10.5px] tracking-[0.07em] text-muted uppercase">
        <span>{t('composer.files.count', { count: props.files.length })}</span>
        <span>{formatBytes(props.totalSize)}</span>
      </div>

      <div class="grid max-h-[360px] content-start gap-1 overflow-auto pr-0.5 sm:max-h-none sm:flex-1">
        <For each={props.files}>
          {(file, index) => (
            <div class="file-row">
              <span class="min-w-0 flex-1 truncate" title={file.name}>
                {file.name}
              </span>
              <span class="shrink-0 text-[10.5px] text-muted">{formatBytes(file.size)}</span>
              <button
                type="button"
                class="shrink-0 cursor-pointer px-0.5 text-[13px] leading-none text-muted transition hover:text-ink"
                aria-label={t('composer.files.remove', { name: file.name })}
                onClick={() => props.onRemove(index())}
              >
                ×
              </button>
            </div>
          )}
        </For>
      </div>

      <p class="mt-2 font-mono text-[10px] text-muted/90">{props.limitLabel ?? ''}</p>
    </div>
  );
}
