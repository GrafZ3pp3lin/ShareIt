import { clsx } from 'clsx/lite';
import { Show, createSignal, onSettled } from 'solid-js';
import { useI18n } from '../../../i18n/i18n.tsx';
import { ComposerFiles } from './composer-files.tsx';

type ComposerProps = {
  text: string;
  files: File[];
  totalSize: number;
  limitLabel?: string;
  onTextInput: (value: string) => void;
  onAddFiles: (files: File[]) => void;
  onRemoveFile: (index: number) => void;
  onSubmitShortcut: () => void;
};

/**
 * One surface for text and files. The textarea keeps a fixed height (long
 * pastes scroll inside), files sit on the right, and every drop target is the
 * whole box. The `+` button stays in the bottom-right corner in every state.
 */
export function Composer(props: ComposerProps) {
  const { t } = useI18n();
  const [dragging, setDragging] = createSignal(false);
  let fileInput: HTMLInputElement | undefined;

  onSettled(() => {
    const preventDocumentDrop = (event: DragEvent) => event.preventDefault();
    document.addEventListener('dragover', preventDocumentDrop);
    document.addEventListener('drop', preventDocumentDrop);

    return () => {
      document.removeEventListener('dragover', preventDocumentDrop);
      document.removeEventListener('drop', preventDocumentDrop);
    };
  });

  const hasFiles = () => props.files.length > 0;

  const appendFromInput = (input: HTMLInputElement) => {
    props.onAddFiles(Array.from(input.files ?? []));
    input.value = '';
  };

  const handleDrop = (event: DragEvent) => {
    event.preventDefault();
    setDragging(false);
    props.onAddFiles(Array.from(event.dataTransfer?.files ?? []));
  };

  return (
    <div
      class="composer"
      onDragOver={(event) => {
        event.preventDefault();
        if (event.dataTransfer) {
          event.dataTransfer.dropEffect = 'copy';
        }
        setDragging(true);
      }}
      onDragLeave={(event) => {
        const target = event.relatedTarget;
        if (target instanceof Node && event.currentTarget.contains(target)) {
          return;
        }
        setDragging(false);
      }}
      onDrop={handleDrop}
    >
      <div class={clsx('grid min-h-0 gap-4', hasFiles() ? 'sm:grid-cols-2' : 'grid-cols-1')}>
        <div class="flex min-h-0 flex-col">
          <textarea
            class="h-[320px] w-full resize-none overflow-auto rounded-lg bg-transparent p-1 pb-14 text-sm leading-relaxed text-ink outline-none placeholder:text-muted/80 sm:h-[var(--composer-height)]"
            placeholder={t('composer.placeholder')}
            aria-label={t('composer.placeholder')}
            value={props.text}
            onInput={(event) => props.onTextInput(event.currentTarget.value)}
            onKeyDown={(event) => {
              if ((event.metaKey || event.ctrlKey) && event.key === 'Enter') {
                event.preventDefault();
                props.onSubmitShortcut();
              }
            }}
          />
          <p class="pointer-events-none -mt-12 px-1 font-mono text-[10.5px] leading-relaxed text-muted/90">
            {t('composer.hint')}
          </p>
        </div>

        <Show when={hasFiles()}>
          <ComposerFiles
            files={props.files}
            totalSize={props.totalSize}
            limitLabel={props.limitLabel}
            onRemove={props.onRemoveFile}
          />
        </Show>
      </div>

      <button
        type="button"
        class="absolute right-3 bottom-3 z-20 grid h-10 w-10 cursor-pointer place-items-center rounded-full bg-ink text-xl leading-none text-panel shadow-button transition hover:bg-brand-hover"
        aria-label={t('composer.addFiles')}
        onClick={() => fileInput?.click()}
      >
        +
      </button>

      <input
        ref={(element) => {
          fileInput = element;
        }}
        class="hidden"
        type="file"
        multiple
        aria-label={t('composer.addFiles')}
        onChange={(event) => appendFromInput(event.currentTarget)}
      />

      <Show when={dragging()}>
        <div class="pointer-events-none absolute inset-0 z-10 grid place-items-center rounded-composer border-2 border-dashed border-ink bg-panel font-mono text-xs tracking-[0.1em] text-ink uppercase">
          {t('composer.drop')}
        </div>
      </Show>
    </div>
  );
}
