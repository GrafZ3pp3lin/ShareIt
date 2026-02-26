import { useMutation } from '@tanstack/solid-query';
import { Show, createSignal } from 'solid-js';
import { UploadFileFields, UploadMetadataFields, UploadTextFields } from '../components/upload-form-fields';
import { UploadHero } from '../components/upload-hero';
import { UploadModeTabs } from '../components/upload-mode-tabs';
import { UploadResultPanel } from '../components/upload-result-panel';
import type { UploadMode, UploadResult } from '../components/upload-types';
import { addTextContent, createContentPackage, uploadFiles } from '../../../shared/api/content';
import { getErrorMessage } from '../../../shared/api/error-message';
import { Button } from '../../../shared/components/button';
import { NoticeBox } from '../../../shared/components/notice-box';
import { SurfaceCard } from '../../../shared/components/surface-card';
import { encryptText, generatePassphrase } from '../../../shared/crypto/crypto';
import { useI18n } from '../../../i18n';

export function UploadPage() {
  const { t } = useI18n();
  const [mode, setMode] = createSignal<UploadMode>('text');
  const [title, setTitle] = createSignal('');
  const [description, setDescription] = createSignal('');
  const [text, setText] = createSignal('');
  const [files, setFiles] = createSignal<File[]>([]);
  const [encrypt, setEncrypt] = createSignal(true);
  const [error, setError] = createSignal<string | null>(null);
  const [result, setResult] = createSignal<UploadResult | null>(null);

  const resetStatus = () => {
    setError(null);
    setResult(null);
  };

  type SubmitResult = UploadResult | { error: string };

  const submitMutation = useMutation(() => ({
    mutationFn: async (): Promise<SubmitResult> => {
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

      if (mode() === 'text') {
        if (!text().trim()) {
          return { error: t('upload.errors.missingText') };
        }
        let payload = text();
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
        const selected = files();
        if (!selected.length) {
          return { error: t('upload.errors.missingFiles') };
        }

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
    <div class="grid items-center gap-7 lg:grid-cols-2">
      <UploadHero
        title={t('upload.heroTitle')}
        description={t('upload.heroDescription')}
        encryptionPill={t('upload.encryptionPill')}
      />
      <SurfaceCard>
        <form class="grid gap-3" onSubmit={handleSubmit}>
          <UploadModeTabs
            mode={mode()}
            onModeChange={setMode}
            textLabel={t('upload.tabs.text')}
            filesLabel={t('upload.tabs.files')}
          />

          <UploadMetadataFields
            title={title()}
            description={description()}
            titlePlaceholder={t('upload.placeholders.title')}
            descriptionPlaceholder={t('upload.placeholders.description')}
            onTitleInput={setTitle}
            onDescriptionInput={setDescription}
          />

          <Show when={mode() === 'text'}>
            <UploadTextFields
              text={text()}
              encrypt={encrypt()}
              textPlaceholder={t('upload.placeholders.text')}
              encryptLabel={t('upload.encryptLabel')}
              onTextInput={setText}
              onEncryptChange={setEncrypt}
            />
          </Show>

          <Show when={mode() === 'file'}>
            <UploadFileFields
              filesCount={files().length}
              filesSelectedLabel={t('upload.filesSelected', { count: files().length })}
              onFilesChange={setFiles}
            />
          </Show>

          <Button type="submit" disabled={submitMutation.isPending}>
            {submitMutation.isPending ? t('upload.submit.uploading') : t('upload.submit.create')}
          </Button>

          <Show when={error()}>
            <NoticeBox>{error()}</NoticeBox>
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
      </SurfaceCard>
    </div>
  );
}
