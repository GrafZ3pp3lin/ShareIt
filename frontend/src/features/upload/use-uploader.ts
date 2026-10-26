import { createMemo, createSignal, onSettled } from 'solid-js';
import type { UploadResult } from './components/upload-types.ts';
import { DEFAULT_EXPIRY_INDEX, EXPIRY_OPTIONS, ANONYMOUS_EXPIRY_INDEX, cycleExpiryIndex } from './upload-expiry.ts';
import {
  DEFAULT_DOWNLOAD_COUNT_INDEX,
  DOWNLOAD_COUNT_OPTIONS,
  ANONYMOUS_DOWNLOAD_COUNT,
  cycleDownloadCountIndex
} from './upload-download-count.ts';
import { createContentPackage, getUploadLimits, uploadFile } from '../../shared/api/content.ts';
import type { UploadLimits } from '../../shared/api/content.ts';
import { getErrorMessage } from '../../shared/api/error-message.ts';
import {
  encryptFile,
  encryptText,
  generatePassphrase,
  ENCRYPTED_FILE_OVERHEAD_BYTES
} from '../../shared/crypto/crypto.ts';
import { useI18n } from '../../i18n/i18n.tsx';
import { useAuth } from '../auth/auth-context.tsx';

type UploadProgress = {
  phase: 'encrypting' | 'uploading';
  index: number;
  total: number;
  fileName: string;
  sentBytes: number;
  totalBytes: number;
};

// State of a package created in step 1 whose files did not all upload, kept so
// the same package can be retried (or its link shown as-is) on recovery.
type PartialPackage = {
  packageId: string;
  nextIndex: number;
  passphrase: string | undefined;
  expiryLabel: string;
  encrypted: boolean;
};

type SubmitResult = UploadResult | { error: string; partial?: PartialPackage };

const toPartial = (
  packageId: string,
  nextIndex: number,
  passphrase: string | undefined,
  encrypted: boolean,
  expiryLabel: string
): PartialPackage => ({ packageId, nextIndex, passphrase, expiryLabel, encrypted });

const formatMb = (bytes: number) => `${Math.round(bytes / (1024 * 1024))} MB`;

/**
 * Owns the upload form state and the two-step upload pipeline (create the
 * package, then encrypt and upload each file with progress). Any edit to the
 * form invalidates a resumable partial upload.
 */
export function createUploader() {
  const { t } = useI18n();
  const { isAuthenticated } = useAuth();
  const [stage, setStage] = createSignal<'form' | 'result'>('form');
  // Raw signal setters; the public setters below clear a resumable partial
  // upload first, so any form edit invalidates recovery.
  const [text, setTextBase] = createSignal('');
  const [files, setFiles] = createSignal<File[]>([]);
  const [encrypt, setEncrypt] = createSignal(true);
  const [expiryIndex, setExpiryIndex] = createSignal(DEFAULT_EXPIRY_INDEX);
  const [downloadCountIndex, setDownloadCountIndex] = createSignal(DEFAULT_DOWNLOAD_COUNT_INDEX);
  const [error, setError] = createSignal<string | null>(null);
  const [result, setResult] = createSignal<UploadResult | null>(null);
  const [progress, setProgress] = createSignal<UploadProgress | null>(null);
  // Step 2 failure after step 1 already created the package: kept so the same
  // package can be retried (or its link shown as-is) instead of silently orphaning it.
  const [partial, setPartial] = createSignal<PartialPackage | null>(null);
  const hasText = createMemo(() => text().trim().length > 0);
  const hasFiles = createMemo(() => files().length > 0);
  // Guests have no expiry picker: they always get the shortest lifetime.
  const expiry = createMemo(() => EXPIRY_OPTIONS[isAuthenticated() ? expiryIndex() : ANONYMOUS_EXPIRY_INDEX]);

  let abortController: AbortController | null = null;

  // File-size limits from the backend (anonymous vs authenticated differ).
  // One explicit load per mount; a failure just leaves the limit unset.
  const [limits, setLimits] = createSignal<UploadLimits | null>(null);
  const [submitting, setSubmitting] = createSignal(false);

  onSettled(() => {
    void getUploadLimits().then(
      (outcome) => {
        if (outcome.data) {
          setLimits(outcome.data);
        }
      },
      () => {
        // a failed limits load just leaves the limit unset
      }
    );
  });

  const fileLimitBytes = createMemo(() => {
    const available = limits();
    if (!available) {
      return null;
    }
    return isAuthenticated() ? available.AuthenticatedFileLimitBytes : available.AnonymousFileLimitBytes;
  });

  const limitLabel = createMemo(() => {
    const limit = fileLimitBytes();
    return limit == null ? undefined : t('upload.filePanel.limit', { limit: formatMb(limit) });
  });

  const clearPartial = () => setPartial(null);

  const setText = (value: string) => {
    clearPartial();
    setTextBase(value);
  };

  const handleFilesChange = (incoming: File[]) => {
    clearPartial();
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

  const toggleEncrypt = () => {
    clearPartial();
    setEncrypt((value) => !value);
  };

  const cycleExpiry = () => {
    clearPartial();
    setExpiryIndex(cycleExpiryIndex);
  };

  const downloadCount = createMemo(() =>
    isAuthenticated() ? DOWNLOAD_COUNT_OPTIONS[downloadCountIndex()] : ANONYMOUS_DOWNLOAD_COUNT
  );

  const cycleDownloadCount = () => {
    clearPartial();
    setDownloadCountIndex(cycleDownloadCountIndex);
  };

  const accessLabel = createMemo(() => {
    const count = downloadCount();
    return count == null ? t('upload.access.unlimited') : String(count);
  });

  const applyResult = (value: SubmitResult) => {
    if ('error' in value) {
      setError(value.error);
      setPartial(value.partial ?? null);
      return;
    }
    setResult(value);
    setStage('result');
  };

  // The upload pipeline: validate, create the package (unless resuming a
  // partial attempt), then encrypt and upload each file with progress.
  const buildUploadResult = async (resume?: PartialPackage): Promise<SubmitResult> => {
    if (!hasText() && !hasFiles()) {
      return { error: t('upload.errors.missingContent') };
    }

    const willEncrypt = resume ? resume.encrypted : encrypt();
    let passphrase: string | undefined;
    if (willEncrypt) {
      passphrase = resume ? resume.passphrase : generatePassphrase();
    }

    let texts: string[] | undefined;
    if (hasText() && !resume) {
      let payload = text().trim();
      if (willEncrypt) {
        try {
          payload = await encryptText(payload, passphrase!);
        } catch (err) {
          return { error: getErrorMessage(err, t('upload.errors.encryptFailed')) };
        }
      }
      texts = [payload];
    }

    abortController = new AbortController();

    // Step 1: create the package with metadata and text inline. Skipped when
    // resuming: the package from the failed attempt is reused as-is.
    let packageId = resume?.packageId;
    if (!packageId) {
      const packageResult = await createContentPackage({
        Title: null,
        Description: null,
        TimeToLiveHours: expiry().hours,
        MaxDownloadCount: downloadCount(),
        Texts: texts
      });

      if (!packageResult.data) {
        return {
          error: getErrorMessage(packageResult.error, t('upload.errors.createPackage'))
        };
      }
      packageId = packageResult.data.Id;
    }

    // Step 2: encrypt and upload each file individually, with progress.
    // Fails soft: the failure is reported with the package state so the same
    // package can be retried instead of leaving an unreachable orphan behind.
    const selected = files();
    for (let index = resume?.nextIndex ?? 0; index < selected.length; index += 1) {
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
            error: getErrorMessage(err, t('upload.errors.encryptFileFailed')),
            partial: toPartial(packageId, index, passphrase, willEncrypt, expiry().label)
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
        return {
          error: t('upload.errors.canceled'),
          partial: toPartial(packageId, index, passphrase, willEncrypt, expiry().label)
        };
      }
      if (!uploadResult.data) {
        return {
          error: getErrorMessage(uploadResult.error, t('upload.errors.uploadFilesFailed')),
          partial: toPartial(packageId, index, passphrase, willEncrypt, expiry().label)
        };
      }
    }

    setProgress(null);
    abortController = null;

    const link = `${window.location.origin}/s/${packageId}`;
    return { link, passphrase, expiryLabel: resume?.expiryLabel ?? expiry().label, encrypted: willEncrypt };
  };

  const runSubmit = async (resume?: PartialPackage): Promise<void> => {
    setSubmitting(true);
    try {
      applyResult(await buildUploadResult(resume));
    } catch (err) {
      setError(getErrorMessage(err, t('upload.errors.createPackage')));
      setPartial(null);
    } finally {
      setSubmitting(false);
    }
  };

  const handleSubmit = (event: Event) => {
    event.preventDefault();
    setError(null);
    setResult(null);
    // A pending partial upload makes the submit button retry into the same
    // package; only then is it safe to resume, otherwise start a new package.
    const resume = partial();
    setPartial(null);
    void runSubmit(resume ?? undefined);
  };

  // Shows the share link for a package with unfinished file uploads: the text
  // (and any files that did upload) is still worth sharing.
  const showLinkAnyway = () => {
    const resume = partial();
    if (!resume) {
      return;
    }
    setPartial(null);
    setError(null);
    setResult({
      link: `${window.location.origin}/s/${resume.packageId}`,
      passphrase: resume.passphrase,
      expiryLabel: resume.expiryLabel,
      encrypted: resume.encrypted,
      incomplete: true
    });
    setStage('result');
  };

  const handleCancel = () => {
    abortController?.abort();
  };

  const resetForm = () => {
    clearPartial();
    setTextBase('');
    setFiles([]);
    setError(null);
    setResult(null);
    setProgress(null);
    setStage('form');
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

  return {
    // state
    stage,
    text,
    files,
    encrypt,
    expiry,
    downloadCount,
    accessLabel,
    isAuthenticated,
    submitting,
    progress,
    progressLabel,
    error,
    partial,
    result,
    limitLabel,
    // actions
    setText,
    handleFilesChange,
    toggleEncrypt,
    cycleExpiry,
    cycleDownloadCount,
    handleSubmit,
    handleCancel,
    showLinkAnyway,
    resetForm
  };
}
