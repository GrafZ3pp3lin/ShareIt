import { useMutation } from '@tanstack/solid-query';
import { Show, createMemo, createSignal } from 'solid-js';
import { UploadContentField, UploadMetadataFields } from '../components/upload-form-fields';
import { UploadHero } from '../components/upload-hero';
import { UploadResultPanel } from '../components/upload-result-panel';
import type { UploadResult } from '../components/upload-types';
import { addTextContent, createContentPackage, uploadFiles } from '../../../shared/api/content';
import { getErrorMessage } from '../../../shared/api/error-message';
import { encryptText, generatePassphrase } from '../../../shared/crypto/crypto';
import { useI18n } from '../../../i18n';

export function UploadPage() {
  const { t } = useI18n();
  const [title, setTitle] = createSignal('');
  const [description, setDescription] = createSignal('');
  const [text, setText] = createSignal('');
  const [files, setFiles] = createSignal<File[]>([]);
  const [encrypt, setEncrypt] = createSignal(true);
  const [error, setError] = createSignal<string | null>(null);
  const [result, setResult] = createSignal<UploadResult | null>(null);
  const hasText = createMemo(() => text().trim().length > 0);
  const hasFiles = createMemo(() => files().length > 0);

  const resetStatus = () => {
    setError(null);
    setResult(null);
  };

  type SubmitResult = UploadResult | { error: string };

  const submitMutation = useMutation(() => ({
    mutationFn: async (): Promise<SubmitResult> => {
      if (!hasText() && !hasFiles()) {
        return { error: t('upload.errors.missingContent') };
      }

      const packageResult = await createContentPackage({
        Title: title() || null,
        Description: description() || null,
      });

      if (!packageResult.data) {
        return {
          error: getErrorMessage(packageResult.error, t('upload.errors.createPackage')),
        };
      }

      let passphrase: string | undefined;

      if (hasText()) {
        let payload = text().trim();
        if (encrypt()) {
          passphrase = generatePassphrase();
          try {
            payload = await encryptText(payload, passphrase);
          } catch (err) {
            return {
              error: getErrorMessage(err, t('upload.errors.encryptFailed')),
            };
          }
        }

        const addTextResult = await addTextContent(packageResult.data.Id, payload);
        if (!addTextResult.data) {
          return {
            error: getErrorMessage(addTextResult.error, t('upload.errors.addTextFailed')),
          };
        }
      } else {
        passphrase = undefined;
      }

      if (hasFiles()) {
        const selected = files();
        const uploadResult = await uploadFiles(packageResult.data.Id, selected);
        if (!uploadResult.data) {
          return {
            error: getErrorMessage(uploadResult.error, t('upload.errors.uploadFilesFailed')),
          };
        }
      }

      const link = `${window.location.origin}/s/${packageResult.data.Id}`;
      return { link, passphrase };
    },
  }));

  const handleSubmit = (event: Event) => {
    event.preventDefault();
    resetStatus();

    submitMutation.mutate(undefined, {
      onSuccess: (value) => {
        if ('error' in value) {
          setError(value.error);
          return;
        }
        setResult(value);
      },
    });
  };

  const copyToClipboard = async (value: string) => {
    try {
      await navigator.clipboard.writeText(value);
    } catch {
      return;
    }
  };

  return (
    <div class="grid gap-7 lg:grid-cols-2">
      <UploadHero
        title={t('upload.heroTitle')}
        description={t('upload.heroDescription')}
        encryptionPill={t('upload.encryptionPill')}
      />
      <div class="surface-card">
        <form class="grid gap-3" onSubmit={handleSubmit}>
          <UploadMetadataFields
            title={title()}
            description={description()}
            titlePlaceholder={t('upload.placeholders.title')}
            descriptionPlaceholder={t('upload.placeholders.description')}
            onTitleInput={setTitle}
            onDescriptionInput={setDescription}
          />

          <UploadContentField
            text={text()}
            encrypt={encrypt()}
            textPlaceholder={t('upload.placeholders.text')}
            encryptLabel={t('upload.encryptLabel')}
            filesCount={files().length}
            dropzoneLabel={t('upload.dropzoneLabel')}
            chooseFilesLabel={t('upload.chooseFiles')}
            filesSelectedLabel={t('upload.filesSelected', { count: files().length })}
            hasText={hasText()}
            hasFiles={hasFiles()}
            onTextInput={setText}
            onEncryptChange={setEncrypt}
            onFilesChange={setFiles}
          />

          <button class="btn btn-primary" type="submit" disabled={submitMutation.isPending}>
            {submitMutation.isPending ? t('upload.submit.uploading') : t('upload.submit.create')}
          </button>

          <Show when={error()}>
            <div class="notice-box">{error()}</div>
          </Show>

          <Show when={result()}>
            {(value) => (
              <UploadResultPanel
                result={value()}
                shareLinkLabel={t('upload.shareLink')}
                copyLinkLabel={t('upload.copyLink')}
                passphraseLabel={t('upload.passphraseLabel')}
                copyPassphraseLabel={t('upload.copyPassphrase')}
                onCopyLink={copyToClipboard}
                onCopyPassphrase={copyToClipboard}
              />
            )}
          </Show>
        </form>
      </div>
    </div>
  );
}
