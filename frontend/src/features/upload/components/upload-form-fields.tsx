import { FileUpload } from '@ark-ui/solid/file-upload';
import { clsx } from 'clsx/lite';
import { Show } from 'solid-js';
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
  filesCount: number;
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
  return (
    <div class="flex flex-col">
      <div class="mb-3.5 font-mono text-xs text-muted">{props.label}</div>
      <FileUpload.Root
        maxFiles={Infinity}
        onFileChange={(details) => props.onFilesChange(details.acceptedFiles)}
        class="flex-1"
      >
        <FileUpload.HiddenInput />
        <FileUpload.Dropzone
          class={clsx(
            'grid h-full min-h-28 place-items-center rounded-xl border border-dashed border-border-soft bg-panel-soft/75 p-4 py-8 text-center text-sm text-muted transition hover:border-ink/30',
            props.filesCount > 0 && 'border-ink/30 bg-panel'
          )}
        >
          <div class="flex flex-col items-center gap-3">
            <div class="flex h-8 w-8 items-center justify-center rounded-md bg-panel-soft">
              <IconUpload class="h-4 w-4 text-muted" />
            </div>
            <div class="text-sm font-medium text-ink">
              <Show when={props.filesCount > 0} fallback={props.promptLabel}>
                {props.selectedLabel}
              </Show>
            </div>
          </div>
        </FileUpload.Dropzone>
      </FileUpload.Root>
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
