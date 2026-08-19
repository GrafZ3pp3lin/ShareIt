import { clsx } from 'clsx/lite';
import { Show, onCleanup, onMount } from 'solid-js';
import IconUpload from '~icons/lucide/upload';

type UploadMetadataFieldsProps = {
  title: string;
  description: string;
  titlePlaceholder: string;
  descriptionPlaceholder: string;
  onTitleInput: (value: string) => void;
  onDescriptionInput: (value: string) => void;
};

type UploadFilePanelProps = {
  label: string;
  promptLabel: string;
  selectedLabel: string;
  files: File[];
  onFilesChange: (files: File[]) => void;
};

type UploadTextPanelProps = {
  label: string;
  placeholder: string;
  text: string;
  hasText: boolean;
  onTextInput: (value: string) => void;
};

export function UploadMetadataFields(props: UploadMetadataFieldsProps) {
  return (
    <>
      <input
        class="text-field"
        type="text"
        placeholder={props.titlePlaceholder}
        value={props.title}
        onInput={(event) => props.onTitleInput(event.currentTarget.value)}
      />
      <input
        class="text-field"
        type="text"
        placeholder={props.descriptionPlaceholder}
        value={props.description}
        onInput={(event) => props.onDescriptionInput(event.currentTarget.value)}
      />
    </>
  );
}

export function UploadFilePanel(props: UploadFilePanelProps) {
  onMount(() => {
    const preventDocumentDrop = (event: DragEvent) => event.preventDefault();
    document.addEventListener('dragover', preventDocumentDrop);
    document.addEventListener('drop', preventDocumentDrop);

    onCleanup(() => {
      document.removeEventListener('dragover', preventDocumentDrop);
      document.removeEventListener('drop', preventDocumentDrop);
    });
  });

  const appendFiles = (files: File[]) => {
    const filesToAdd = files.filter((file, index) => {
      const isAlreadySelected = props.files.some(
        (selectedFile) =>
          selectedFile.name === file.name && selectedFile.size === file.size && selectedFile.type === file.type
      );
      const isRepeatedInBatch = files
        .slice(0, index)
        .some(
          (previousFile) =>
            previousFile.name === file.name && previousFile.size === file.size && previousFile.type === file.type
        );
      return !isAlreadySelected && !isRepeatedInBatch;
    });

    if (filesToAdd.length > 0) {
      props.onFilesChange([...props.files, ...filesToAdd]);
    }
  };

  return (
    <div class="flex flex-col">
      <div class="mb-3.5 font-mono text-xs text-muted">{props.label}</div>
      <label
        class={clsx(
          'grid h-full min-h-28 flex-1 cursor-pointer place-items-center rounded-xl border border-dashed border-border-soft bg-panel-soft/75 p-4 py-8 text-center text-sm text-muted transition hover:border-ink/30 focus-within:ring-2 focus-within:ring-ink/25',
          props.files.length > 0 && 'border-ink/30 bg-panel'
        )}
        onDragOver={(event) => {
          event.preventDefault();
          if (event.dataTransfer) {
            event.dataTransfer.dropEffect = 'copy';
          }
        }}
        onDrop={(event) => {
          event.preventDefault();
          appendFiles(Array.from(event.dataTransfer?.files ?? []));
        }}
      >
        <input
          class="sr-only"
          type="file"
          multiple
          aria-label={props.label}
          onChange={(event) => {
            appendFiles(Array.from(event.currentTarget.files ?? []));
            event.currentTarget.value = '';
          }}
        />
        <div class="flex flex-col items-center gap-3">
          <div class="flex h-8 w-8 items-center justify-center rounded-md bg-panel-soft">
            <IconUpload class="h-4 w-4 text-muted" />
          </div>
          <div class="text-sm font-medium text-ink">
            <Show when={props.files.length > 0} fallback={props.promptLabel}>
              {props.selectedLabel}
            </Show>
          </div>
        </div>
      </label>
    </div>
  );
}

export function UploadTextPanel(props: UploadTextPanelProps) {
  return (
    <div class="flex flex-col">
      <div class="mb-3.5 font-mono text-xs text-muted">{props.label}</div>
      <textarea
        class={clsx(
          'flex-1 w-full resize-none overflow-y-auto rounded-xl border border-border-soft bg-panel-soft/45 p-3 text-sm font-sans text-ink placeholder:text-muted/80 transition focus:border-ink/30 focus:bg-panel focus:outline-none',
          props.hasText && 'border-ink/30 bg-panel'
        )}
        placeholder={props.placeholder}
        value={props.text}
        onInput={(event) => props.onTextInput(event.currentTarget.value)}
      />
    </div>
  );
}
