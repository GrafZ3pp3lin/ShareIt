import { fireEvent, render, screen } from '@solidjs/testing-library';
import { describe, expect, it, vi } from 'vitest';

import { UploadFilePanel, UploadMetadataFields, UploadTextPanel } from './upload-form-fields.tsx';

const makeFile = (name: string, type = 'text/plain') => new File(['abc'], name, { type });

const fireFileChange = (input: HTMLInputElement, files: File[]) => {
  Object.defineProperty(input, 'files', { value: files, configurable: true });
  fireEvent(input, new Event('change', { bubbles: true }));
};

describe('UploadFilePanel', () => {
  it('shows the prompt label when no files are selected', () => {
    render(() => (
      <UploadFilePanel
        label="FILE"
        promptLabel="Drop file or browse"
        selectedLabel="1 file(s) selected"
        files={[]}
        onFilesChange={() => {}}
      />
    ));

    expect(screen.getByText('Drop file or browse')).toBeInTheDocument();
    expect(screen.queryByText('1 file(s) selected')).not.toBeInTheDocument();
  });

  it('shows the selected label when files are present', () => {
    render(() => (
      <UploadFilePanel
        label="FILE"
        promptLabel="Drop file or browse"
        selectedLabel="2 file(s) selected"
        files={[makeFile('a.txt'), makeFile('b.txt')]}
        onFilesChange={() => {}}
      />
    ));

    expect(screen.getByText('2 file(s) selected')).toBeInTheDocument();
  });

  it('forwards files picked via the file input', () => {
    const onFilesChange = vi.fn();
    const { container } = render(() => (
      <UploadFilePanel
        label="FILE"
        promptLabel="prompt"
        selectedLabel="selected"
        files={[]}
        onFilesChange={onFilesChange}
      />
    ));

    const input = container.querySelector<HTMLInputElement>(`input[type="file"]`)!;
    fireFileChange(input, [makeFile('a.txt')]);

    expect(onFilesChange).toHaveBeenCalledWith([expect.objectContaining({ name: 'a.txt' })]);
  });

  it('does not forward a change that adds no new files', () => {
    const onFilesChange = vi.fn();
    const existing = makeFile('a.txt');
    const added = makeFile('b.txt');
    const { container } = render(() => (
      <UploadFilePanel
        label="FILE"
        promptLabel="prompt"
        selectedLabel="selected"
        files={[existing]}
        onFilesChange={onFilesChange}
      />
    ));

    const input = container.querySelector<HTMLInputElement>(`input[type="file"]`)!;
    fireFileChange(input, [makeFile('a.txt'), added]);

    expect(onFilesChange).toHaveBeenCalledWith([existing, added]);
  });

  it('does not forward a change that contains only duplicates', () => {
    const onFilesChange = vi.fn();
    const { container } = render(() => (
      <UploadFilePanel
        label="FILE"
        promptLabel="prompt"
        selectedLabel="selected"
        files={[makeFile('a.txt')]}
        onFilesChange={onFilesChange}
      />
    ));

    const input = container.querySelector<HTMLInputElement>(`input[type="file"]`)!;
    fireFileChange(input, [makeFile('a.txt')]);

    expect(onFilesChange).not.toHaveBeenCalled();
  });
});

describe('UploadTextPanel', () => {
  it('renders the label and placeholder', () => {
    render(() => (
      <UploadTextPanel
        label="TEXT / SNIPPET"
        placeholder="Paste or type your text"
        text=""
        hasText={false}
        onTextInput={() => {}}
      />
    ));

    expect(screen.getByText('TEXT / SNIPPET')).toBeInTheDocument();
    expect(screen.getByPlaceholderText('Paste or type your text')).toBeInTheDocument();
  });

  it('forwards text input', () => {
    const onTextInput = vi.fn();
    render(() => (
      <UploadTextPanel
        label="TEXT"
        placeholder="Paste or type your text"
        text=""
        hasText={false}
        onTextInput={onTextInput}
      />
    ));

    fireEvent.input(screen.getByPlaceholderText('Paste or type your text'), { target: { value: 'hello' } });

    expect(onTextInput).toHaveBeenCalledWith('hello');
  });

  it('highlights the textarea when it has text', () => {
    render(() => (
      <UploadTextPanel
        label="TEXT"
        placeholder="Paste or type your text"
        text="filled"
        hasText={true}
        onTextInput={() => {}}
      />
    ));

    expect(screen.getByPlaceholderText('Paste or type your text').className).toContain('border-ink/30');
  });
});

describe('UploadMetadataFields', () => {
  it('forwards title and description input', () => {
    const onTitleInput = vi.fn();
    const onDescriptionInput = vi.fn();
    render(() => (
      <UploadMetadataFields
        title=""
        description=""
        titlePlaceholder="Title (optional)"
        descriptionPlaceholder="Description (optional)"
        onTitleInput={onTitleInput}
        onDescriptionInput={onDescriptionInput}
      />
    ));

    fireEvent.input(screen.getByPlaceholderText('Title (optional)'), { target: { value: 'my title' } });
    fireEvent.input(screen.getByPlaceholderText('Description (optional)'), { target: { value: 'my desc' } });

    expect(onTitleInput).toHaveBeenCalledWith('my title');
    expect(onDescriptionInput).toHaveBeenCalledWith('my desc');
  });
});
