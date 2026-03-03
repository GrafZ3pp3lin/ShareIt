import { FileUpload } from '@ark-ui/solid/file-upload';
import { clsx } from 'clsx/lite';
import { Show } from 'solid-js';

type UploadMetadataFieldsProps = {
  title: string;
  description: string;
  titlePlaceholder: string;
  descriptionPlaceholder: string;
  onTitleInput: (value: string) => void;
  onDescriptionInput: (value: string) => void;
};

type UploadContentFieldProps = {
  text: string;
  encrypt: boolean;
  textPlaceholder: string;
  encryptLabel: string;
  filesCount: number;
  dropzoneLabel: string;
  chooseFilesLabel: string;
  filesSelectedLabel?: string;
  hasText: boolean;
  hasFiles: boolean;
  onTextInput: (value: string) => void;
  onEncryptChange: (value: boolean) => void;
  onFilesChange: (files: File[]) => void;
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

export function UploadContentField(props: UploadContentFieldProps) {
  return (
    <div class="grid gap-3">
      <FileUpload.Root maxFiles={Infinity} onFileChange={(details) => props.onFilesChange(details.acceptedFiles)}>
        <FileUpload.HiddenInput />

        <FileUpload.Dropzone class={clsx('content-dropzone-field', props.hasFiles && 'content-dropzone-field-active')}>
          <textarea
            class={clsx('content-textarea-field', props.hasText && 'content-textarea-field-active')}
            placeholder={props.textPlaceholder}
            value={props.text}
            onInput={(event) => props.onTextInput(event.currentTarget.value)}
          />
          <div>{props.dropzoneLabel}</div>
        </FileUpload.Dropzone>

        <FileUpload.Trigger type="button" class="btn btn-secondary">
          {props.chooseFilesLabel}
        </FileUpload.Trigger>

        <label class="pill-badge cursor-pointer text-xs">
          <input
            type="checkbox"
            checked={props.encrypt}
            onChange={(event) => props.onEncryptChange(event.currentTarget.checked)}
          />
          {props.encryptLabel}
        </label>

        <Show when={props.filesCount > 0}>
          <div class="text-sm text-muted">{props.filesSelectedLabel}</div>
        </Show>
      </FileUpload.Root>
    </div>
  );
}
