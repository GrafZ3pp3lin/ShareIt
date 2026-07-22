import { useMutation } from '@tanstack/solid-query';
import { Show, createMemo, createSignal } from 'solid-js';
import { UploadFilePanel, UploadMetadataFields, UploadTextPanel } from '../components/upload-form-fields.tsx';
import { UploadResultPanel } from '../components/upload-result-panel.tsx';
import type { UploadResult } from '../components/upload-types.ts';
import { DEFAULT_EXPIRY_INDEX, EXPIRY_OPTIONS, cycleExpiryIndex } from '../upload-expiry.ts';
import { addTextContent, createContentPackage, uploadFiles } from '../../../shared/api/content.ts';
import { getErrorMessage } from '../../../shared/api/error-message.ts';
import { encryptText, generatePassphrase } from '../../../shared/crypto/crypto.ts';
import { TerminalWindow } from '../../../shared/components/terminal-window.tsx';
import { useI18n } from '../../../i18n/i18n.tsx';

export function UploadPage() {
  const { t } = useI18n();
  const [stage, setStage] = createSignal<'form' | 'result'>('form');
  const [title, setTitle] = createSignal('');
  const [description, setDescription] = createSignal('');
  const [text, setText] = createSignal('');
  const [files, setFiles] = createSignal<File[]>([]);
  const [encrypt, setEncrypt] = createSignal(true);
  const [expiryIndex, setExpiryIndex] = createSignal(DEFAULT_EXPIRY_INDEX);
  const [error, setError] = createSignal<string | null>(null);
  const [result, setResult] = createSignal<UploadResult | null>(null);
  const hasText = createMemo(() => text().trim().length > 0);
  const hasFiles = createMemo(() => files().length > 0);
  const expiry = createMemo(() => EXPIRY_OPTIONS[expiryIndex()]);

  type SubmitResult = UploadResult | { error: string };

  const submitMutation = useMutation(() => ({
    mutationFn: async (): Promise<SubmitResult> => {
      if (!hasText() && !hasFiles()) {
        return { error: t('upload.errors.missingContent') };
      }

      const packageResult = await createContentPackage({
        Title: title() || null,
        Description: description() || null,
        TimeToLiveHours: expiry().hours
      });

      if (!packageResult.data) {
        return {
          error: getErrorMessage(packageResult.error, t('upload.errors.createPackage'))
        };
      }

      let passphrase: string | undefined;
      const willEncrypt = hasText() && encrypt();

      if (hasText()) {
        let payload = text().trim();
        if (encrypt()) {
          passphrase = generatePassphrase();
          try {
            payload = await encryptText(payload, passphrase);
          } catch (err) {
            return {
              error: getErrorMessage(err, t('upload.errors.encryptFailed'))
            };
          }
        }

        const addTextResult = await addTextContent(packageResult.data.Id, payload);
        if (!addTextResult.data) {
          return {
            error: getErrorMessage(addTextResult.error, t('upload.errors.addTextFailed'))
          };
        }
      }

      if (hasFiles()) {
        const selected = files();
        const uploadResult = await uploadFiles(packageResult.data.Id, selected);
        if (!uploadResult.data) {
          return {
            error: getErrorMessage(uploadResult.error, t('upload.errors.uploadFilesFailed'))
          };
        }
      }

      const link = `${window.location.origin}/s/${packageResult.data.Id}`;
      return { link, passphrase, expiryLabel: expiry().label, encrypted: willEncrypt };
    }
  }));

  const handleSubmit = (event: Event) => {
    event.preventDefault();
    setError(null);
    setResult(null);

    submitMutation.mutate(undefined, {
      onSuccess: (value) => {
        if ('error' in value) {
          setError(value.error);
          return;
        }
        setResult(value);
        setStage('result');
      }
    });
  };

  const resetForm = () => {
    setTitle('');
    setDescription('');
    setText('');
    setFiles([]);
    setError(null);
    setResult(null);
    submitMutation.reset();
    setStage('form');
  };

  const copyToClipboard = async (value: string) => {
    try {
      await navigator.clipboard.writeText(value);
    } catch {
      return;
    }
  };

  const resultCaption = createMemo(() => {
    const value = result();
    if (!value) {
      return '';
    }
    return t(value.encrypted ? 'upload.result.captionWithPassword' : 'upload.result.captionPlain', {
      expiry: value.expiryLabel
    });
  });

  return (
    <div class="mx-auto w-full max-w-2xl">
      <TerminalWindow label={t('upload.windowLabel')}>
        <Show when={stage() === 'form'}>
          <form class="grid gap-5" onSubmit={handleSubmit}>
            <details>
              <summary class="cursor-pointer font-mono text-xs text-muted transition hover:text-ink">
                {t('upload.details.toggle')}
              </summary>
              <div class="mt-3 grid gap-3">
                <UploadMetadataFields
                  title={title()}
                  description={description()}
                  titlePlaceholder={t('upload.placeholders.title')}
                  descriptionPlaceholder={t('upload.placeholders.description')}
                  onTitleInput={setTitle}
                  onDescriptionInput={setDescription}
                />
              </div>
            </details>

            <div class="grid gap-4 sm:grid-cols-2 min-h-42">
              <UploadFilePanel
                label={t('upload.panels.fileLabel')}
                promptLabel={t('upload.filePanel.prompt')}
                selectedLabel={t('upload.filePanel.selected', { count: files().length })}
                filesCount={files().length}
                onFilesChange={setFiles}
              />
              <UploadTextPanel
                label={t('upload.panels.textLabel')}
                placeholder={t('upload.placeholders.text')}
                text={text()}
                hasText={hasText()}
                onTextInput={setText}
              />
            </div>

            <div class="grid gap-2">
              <div class="flex flex-wrap items-center gap-3">
                <button
                  type="button"
                  class="pill-badge cursor-pointer"
                  onClick={() => setExpiryIndex(cycleExpiryIndex)}
                >
                  <span>{t('upload.expiry.label')}:</span>
                  <span class="text-ink">{expiry().label}</span>
                </button>
                <button type="button" class="pill-badge cursor-pointer" onClick={() => setEncrypt((value) => !value)}>
                  <span>{t('upload.password.label')}:</span>
                  <span class="text-ink">{encrypt() ? t('upload.password.on') : t('upload.password.off')}</span>
                </button>
              </div>
              <p class="text-xs text-muted">{t('upload.password.caption')}</p>
            </div>

            <button class="btn btn-primary" type="submit" disabled={submitMutation.isPending}>
              {submitMutation.isPending ? t('upload.submit.uploading') : t('upload.submitCommand')}
            </button>

            <Show when={error()}>
              <div class="notice-box">{error()}</div>
            </Show>
          </form>
        </Show>

        <Show when={stage() === 'result' ? result() : null}>
          {(value) => (
            <UploadResultPanel
              result={value()}
              headingLabel={t('upload.result.heading')}
              caption={resultCaption()}
              copyLabel={t('upload.result.copy')}
              copiedLabel={t('upload.result.copied')}
              passphraseLabel={t('upload.passphraseLabel')}
              copyPassphraseLabel={t('upload.copyPassphrase')}
              anotherLabel={t('upload.result.another')}
              onCopyLink={copyToClipboard}
              onCopyPassphrase={copyToClipboard}
              onReset={resetForm}
            />
          )}
        </Show>
      </TerminalWindow>
    </div>
  );
}
