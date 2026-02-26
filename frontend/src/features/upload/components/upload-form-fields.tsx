import { Show } from 'solid-js';
import { PillBadge } from '../../../shared/components/pill-badge';
import { TextAreaField, TextField } from '../../../shared/components/text-field';

type UploadMetadataFieldsProps = {
  title: string;
  description: string;
  titlePlaceholder: string;
  descriptionPlaceholder: string;
  onTitleInput: (value: string) => void;
  onDescriptionInput: (value: string) => void;
};

type UploadTextFieldsProps = {
  text: string;
  encrypt: boolean;
  textPlaceholder: string;
  encryptLabel: string;
  onTextInput: (value: string) => void;
  onEncryptChange: (value: boolean) => void;
};

type UploadFileFieldsProps = {
  filesCount: number;
  filesSelectedLabel?: string;
  onFilesChange: (files: File[]) => void;
};

export function UploadMetadataFields(props: UploadMetadataFieldsProps) {
  return (
    <>
      <TextField
        type="text"
        placeholder={props.titlePlaceholder}
        value={props.title}
        onInput={(event) => props.onTitleInput(event.currentTarget.value)}
      />
      <TextField
        type="text"
        placeholder={props.descriptionPlaceholder}
        value={props.description}
        onInput={(event) => props.onDescriptionInput(event.currentTarget.value)}
      />
    </>
  );
}

export function UploadTextFields(props: UploadTextFieldsProps) {
  return (
    <>
      <TextAreaField
        class="min-h-[140px] resize-y"
        placeholder={props.textPlaceholder}
        value={props.text}
        onInput={(event) => props.onTextInput(event.currentTarget.value)}
      />
      <PillBadge as="label" class="cursor-pointer text-xs">
        <input
          type="checkbox"
          checked={props.encrypt}
          onChange={(event) => props.onEncryptChange(event.currentTarget.checked)}
        />
        {props.encryptLabel}
      </PillBadge>
    </>
  );
}

export function UploadFileFields(props: UploadFileFieldsProps) {
  return (
    <>
      <TextField
        type="file"
        multiple
        onChange={(event) => props.onFilesChange(Array.from(event.currentTarget.files ?? []))}
      />
      <Show when={props.filesCount > 0}>
        <div class="text-sm text-muted">{props.filesSelectedLabel}</div>
      </Show>
    </>
  );
}
