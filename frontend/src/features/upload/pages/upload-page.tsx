import { useMutation, useQuery } from '@tanstack/solid-query';
import { Show, createMemo, createSignal } from 'solid-js';
import { UploadFilePanel, UploadMetadataFields, UploadTextPanel } from '../components/upload-form-fields.tsx';
import { UploadResultPanel } from '../components/upload-result-panel.tsx';
import type { UploadResult } from '../components/upload-types.ts';
import { DEFAULT_EXPIRY_INDEX, EXPIRY_OPTIONS, cycleExpiryIndex } from '../upload-expiry.ts';
import { createContentPackage, getUploadLimits, uploadFile } from '../../../shared/api/content.ts';
import { getErrorMessage } from '../../../shared/api/error-message.ts';
import {
  encryptFile,
  encryptText,
  generatePassphrase,
  ENCRYPTED_FILE_OVERHEAD_BYTES
} from '../../../shared/crypto/crypto.ts';
import { TerminalWindow } from '../../../shared/components/terminal-window.tsx';
import { useI18n } from '../../../i18n/i18n.tsx';
import { useAuth } from '../../auth/auth-context.tsx';

type UploadProgress = {
  phase: 'encrypting' | 'uploading';
  index: number;
  total: number;
  fileName: string;
  sentBytes: number;
  totalBytes: number;
};

const formatMb = (bytes: number) => `${Math.round(bytes / (1024 * 1024))} MB`;

export function UploadPage() {
  const { t } = useI18n();
  const { isAuthenticated } = useAuth();
  const [stage, setStage] = createSignal<'form' | 'result'>('form');
  const [title, setTitle] = createSignal('');
  const [description, setDescription] = createSignal('');
  const [text, setText] = createSignal('');
  const [files, setFiles] = createSignal<File[]>([]);
  const [encrypt, setEncrypt] = createSignal(true);
  const [expiryIndex, setExpiryIndex] = createSignal(DEFAULT_EXPIRY_INDEX);
  const [error, setError] = createSignal<string | null>(null);
  const [result, setResult] = createSignal<UploadResult | null>(null);
  const [progress, setProgress] = createSignal<UploadProgress | null>(null);
  const hasText = createMemo(() => text().trim().length > 0);
  const hasFiles = createMemo(() => files().length > 0);
  const expiry = createMemo(() => EXPIRY_OPTIONS[expiryIndex()]);

  type SubmitResult = UploadResult | { error: string };

  let abortController: AbortController | null = null;

  const limitsQuery = useQuery(() => ({
    queryKey: ['upload-limits'],
    queryFn: getUploadLimits,
    staleTime: Number.POSITIVE_INFINITY
  }));

  const fileLimitBytes = createMemo(() => {
    const limits = limitsQuery.data?.data;
    if (!limits) {
      return null;
    }
    return isAuthenticated ? limits.AuthenticatedFileLimitBytes : limits.AnonymousFileLimitBytes;
  });

  const handleFilesChange = (incoming: File[]) => {
    const rawLimit = fileLimitBytes();
    if (rawLimit == null) {
      setFiles(incoming);
      return;
    }

    // Encrypted files carry envelope bytes on top of the plaintext payload.
    const limit = encrypt() ? rawLimit - ENCRYPTED_FILE_OVERHEAD_BYTES : rawLimit;
    const oversized = incoming.filter((file) => file.size > limit);
    setFiles(incoming.filter((file) => file.size <= limit));
    if (oversized.length > 0) {
      setError(
        t('upload.errors.fileTooLarge', {
          names: oversized.map((file) => file.name).join(', '),
          limit: formatMb(limit)
        })
      );
    } else {
      setError(null);
    }
  };

  const submitMutation = useMutation(() => ({
    mutationFn: async (): Promise<SubmitResult> => {
      if (!hasText() && !hasFiles()) {
        return { error: t('upload.errors.missingContent') };
      }

      const willEncrypt = encrypt();
      let passphrase: string | undefined;
      if (willEncrypt) {
        passphrase = generatePassphrase();
      }

      let texts: string[] | undefined;
      if (hasText()) {
        let payload = text().trim();
        if (willEncrypt) {
          try {
            payload = await encryptText(payload, passphrase!);
          } catch (err) {
            return {
              error: getErrorMessage(err, t('upload.errors.encryptFailed'))
            };
          }
        }
        texts = [payload];
      }

      abortController = new AbortController();

      // Step 1: create the package with metadata and text inline.
      const packageResult = await createContentPackage({
        Title: title() || null,
        Description: description() || null,
        TimeToLiveHours: expiry().hours,
        Texts: texts
      });

      if (!packageResult.data) {
        return {
          error: getErrorMessage(packageResult.error, t('upload.errors.createPackage'))
        };
      }
      const packageId = packageResult.data.Id;

      // Step 2: encrypt and upload each file individually, with progress.
      const selected = files();
      for (let index = 0; index < selected.length; index += 1) {
        let file = selected[index];
        setProgress({
          phase: 'encrypting',
          index,
          total: selected.length,
          fileName: file.name,
          sentBytes: 0,
          totalBytes: file.size
        });

        if (willEncrypt) {
          try {
            // oxlint-disable-next-line no-await-in-loop -- files are encrypted one at a time for progress/cancel
            file = await encryptFile(file, passphrase!);
          } catch (err) {
            return {
              error: getErrorMessage(err, t('upload.errors.encryptFileFailed'))
            };
          }
        }

        setProgress({
          phase: 'uploading',
          index,
          total: selected.length,
          fileName: file.name,
          sentBytes: 0,
          totalBytes: file.size
        });

        // oxlint-disable-next-line no-await-in-loop -- files are uploaded one at a time for progress/cancel
        const uploadResult = await uploadFile(
          packageId,
          file,
          (sentBytes, totalBytes) => {
            setProgress((current) => (current ? { ...current, phase: 'uploading', sentBytes, totalBytes } : current));
          },
          abortController.signal
        );

        if (uploadResult.canceled) {
          return { error: t('upload.errors.canceled') };
        }
        if (!uploadResult.data) {
          return {
            error: getErrorMessage(uploadResult.error, t('upload.errors.uploadFilesFailed'))
          };
        }
      }

      setProgress(null);
      abortController = null;

      const link = `${window.location.origin}/s/${packageId}`;
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

  const handleCancel = () => {
    abortController?.abort();
  };

  const resetForm = () => {
    setTitle('');
    setDescription('');
    setText('');
    setFiles([]);
    setError(null);
    setResult(null);
    setProgress(null);
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

  const progressLabel = createMemo(() => {
    const current = progress();
    if (!current) {
      return '';
    }
    if (current.phase === 'encrypting') {
      return t('upload.submit.encrypting', {
        current: current.index + 1,
        total: current.total,
        fileName: current.fileName
      });
    }
    const percent = current.totalBytes > 0 ? Math.round((current.sentBytes / current.totalBytes) * 100) : 0;
    return t('upload.submit.uploadingFile', {
      current: current.index + 1,
      total: current.total,
      percent
    });
  });

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
                files={files()}
                onFilesChange={handleFilesChange}
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
                <Show when={fileLimitBytes()}>
                  {(limit) => (
                    <span class="text-xs text-muted">{t('upload.filePanel.limit', { limit: formatMb(limit()) })}</span>
                  )}
                </Show>
              </div>
              <p class="text-xs text-muted">{t('upload.password.caption')}</p>
            </div>

            <button class="btn btn-primary" type="submit" disabled={submitMutation.isPending}>
              {submitMutation.isPending ? t('upload.submit.uploading') : t('upload.submitCommand')}
            </button>

            <Show when={progress()}>
              <div class="flex items-center justify-between gap-3 font-mono text-xs text-muted">
                <span>{progressLabel()}</span>
                <button class="cursor-pointer underline transition hover:text-ink" type="button" onClick={handleCancel}>
                  {t('upload.submit.cancel')}
                </button>
              </div>
            </Show>

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
