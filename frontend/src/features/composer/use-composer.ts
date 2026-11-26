import { createMemo, createSignal, onSettled } from 'solid-js';
import { createPackage, getUploadLimits, uploadItem } from '../../shared/api/content.ts';
import type { UploadItemResult, UploadLimits } from '../../shared/api/content.ts';
import { getErrorMessage } from '../../shared/api/error-message.ts';
import { toBase64Url } from '../../shared/crypto/base64url.ts';
import { utf8 } from '../../shared/crypto/bytes.ts';
import { encryptFile, sealedSizeOf } from '../../shared/crypto/framed.ts';
import {
  deriveKeyBits,
  frameAad,
  itemKey,
  packageSalt,
  unlockProof,
  type PackageKey
} from '../../shared/crypto/keys.ts';
import { encodeMeta, type ItemMeta } from '../../shared/crypto/meta.ts';
import { generatePassphrase, validatePassphrase } from '../../shared/crypto/passphrase.ts';
import { generateCodeSecret } from '../../shared/short-code.ts';
import { saveShortCode } from '../../shared/short-code-store.ts';
import { useI18n } from '../../i18n/i18n.tsx';
import { useAuth } from '../auth/auth-context.tsx';
import {
  ANONYMOUS_ACCESS_COUNT,
  ANONYMOUS_EXPIRY_INDEX,
  DEFAULT_ACCESS_COUNT_INDEX,
  DEFAULT_EXPIRY_INDEX,
  ACCESS_COUNT_OPTIONS,
  EXPIRY_OPTIONS
} from './composer-options.ts';

export type PasswordMode = 'off' | 'generate' | 'custom' | 'code';

// Packages are encrypted by default; the password can be switched off explicitly.
const DEFAULT_PASSWORD_MODE: PasswordMode = 'generate';

export type ComposerResult = {
  link: string;
  /** Combined six-character access code, when the package is in code mode. */
  code?: string;
  linkWithPassphrase?: string;
  passphrase?: string;
  expiryLabel: string;
  accessesLabel: string;
  passwordLabel: string;
};

export type ComposerProgress = {
  phase: 'encrypting' | 'uploading';
  index: number;
  total: number;
  fileName: string;
  sentBytes: number;
  totalBytes: number;
};

type ComposerItem =
  | { id: string; kind: 'text'; bytes: Uint8Array<ArrayBuffer>; meta: ItemMeta }
  | { id: string; kind: 'file'; file: File; meta: ItemMeta };

type PartialPackage = {
  packageId: string;
  items: ComposerItem[];
  nextIndex: number;
  passphrase: string | undefined;
  /** Combined six-character code, once the server returned the locator. */
  code?: string;
  expiryLabel: string;
  accessesLabel: string;
  passwordLabel: string;
};

const formatMb = (bytes: number) => `${Math.round(bytes / (1024 * 1024))} MB`;

/**
 * Owns the composer form state and the upload pipeline: generate ids, derive
 * the package key (when a passphrase is set), seal every item and its metadata,
 * create the package, then upload one item per request with progress.
 *
 * Ids are generated once per attempt and kept in the partial state, so a retry
 * reuses the same package and item ids (the backend treats both as idempotent).
 * Sealing is deterministic: retries produce identical frames and metadata.
 */
export function createComposer() {
  const { t } = useI18n();
  const { isAuthenticated } = useAuth();

  const [stage, setStage] = createSignal<'form' | 'result'>('form');
  const [text, setTextBase] = createSignal('');
  const [files, setFiles] = createSignal<File[]>([]);
  const [optionsOpen, setOptionsOpen] = createSignal(false);
  const [passwordMode, setPasswordModeBase] = createSignal<PasswordMode>(DEFAULT_PASSWORD_MODE);
  const [customPassphrase, setCustomPassphraseBase] = createSignal('');
  const [generatedPassphrase, setGeneratedPassphrase] = createSignal(generatePassphrase());
  const [expiryIndex, setExpiryIndex] = createSignal(DEFAULT_EXPIRY_INDEX);
  const [accessIndex, setAccessIndex] = createSignal(DEFAULT_ACCESS_COUNT_INDEX);
  const [error, setError] = createSignal<string | null>(null);
  const [result, setResult] = createSignal<ComposerResult | null>(null);
  const [progress, setProgress] = createSignal<ComposerProgress | null>(null);
  const [partial, setPartial] = createSignal<PartialPackage | null>(null);
  const [submitting, setSubmitting] = createSignal(false);
  const [limits, setLimits] = createSignal<UploadLimits | null>(null);

  let abortController: AbortController | null = null;

  const hasText = createMemo(() => text().trim().length > 0);
  const hasFiles = createMemo(() => files().length > 0);
  const hasContent = createMemo(() => hasText() || hasFiles());

  const codeMode = createMemo(() => passwordMode() === 'code');

  // Guests have no pickers: they always get the shortest lifetime and one access.
  const expiry = createMemo(() => EXPIRY_OPTIONS[isAuthenticated() ? expiryIndex() : ANONYMOUS_EXPIRY_INDEX]);
  const accessCount = createMemo(() =>
    isAuthenticated() ? ACCESS_COUNT_OPTIONS[accessIndex()] : ANONYMOUS_ACCESS_COUNT
  );

  const expiryLabel = createMemo(() => (codeMode() ? t('options.expiry.tenMinutes') : t(expiry().labelKey)));
  const accessesLabel = createMemo(() => {
    if (codeMode()) {
      return t('options.downloads.once');
    }
    const count = accessCount();
    if (count == null) {
      return t('options.downloads.unlimited');
    }
    return count === 1 ? t('options.downloads.once') : t('options.downloads.count', { count });
  });
  const passwordLabel = createMemo(() => {
    const mode = passwordMode();
    if (mode === 'generate') {
      return t('options.password.generated');
    }
    if (mode === 'custom') {
      return t('options.password.custom');
    }
    return mode === 'code' ? t('options.password.code') : t('options.password.off');
  });
  const summary = createMemo(() =>
    t('options.summary', {
      expiry: expiryLabel(),
      downloads: accessesLabel(),
      password: passwordLabel()
    })
  );

  const passphrase = createMemo(() => {
    const mode = passwordMode();
    if (mode === 'generate') {
      return generatedPassphrase();
    }
    return mode === 'custom' ? customPassphrase() : undefined;
  });
  const passphraseCheck = createMemo(() => validatePassphrase(customPassphrase()));
  const passphraseError = createMemo(() =>
    passwordMode() === 'custom' && !passphraseCheck().valid ? t('options.password.errors.tooShort') : null
  );

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

  const maxItemsPerPackage = createMemo(() => limits()?.MaxItemsPerPackage ?? null);

  const limitLabel = createMemo(() => {
    const limit = fileLimitBytes();
    return limit == null ? undefined : t('composer.limit', { limit: formatMb(limit) });
  });

  const canSubmit = createMemo(() => hasContent() && !submitting() && passphraseError() === null);

  /**
   * Stored size of one item after sealing. The server limit applies to the
   * stored (encrypted) bytes, which are the plaintext plus one frame header
   * and GCM tag per 4 MiB chunk.
   */
  const storedSizeOf = (item: ComposerItem) =>
    passwordMode() === 'off'
      ? item.kind === 'file'
        ? item.file.size
        : item.bytes.length
      : sealedSizeOf(item.kind === 'file' ? item.file.size : item.bytes.length);

  const clearPartial = () => setPartial(null);

  const setText = (value: string) => {
    clearPartial();
    setError(null);
    setTextBase(value);
  };

  const addFiles = (incoming: File[]) => {
    clearPartial();
    const unique = incoming.filter((file, index) => {
      const isSelected = files().some(
        (selected) => selected.name === file.name && selected.size === file.size && selected.type === file.type
      );
      const isRepeated = incoming
        .slice(0, index)
        .some((previous) => previous.name === file.name && previous.size === file.size && previous.type === file.type);
      return !isSelected && !isRepeated;
    });

    const empty = unique.filter((file) => file.size === 0);
    const nonEmpty = unique.filter((file) => file.size > 0);
    const limit = fileLimitBytes();
    const maxItems = maxItemsPerPackage();
    if (limit == null && maxItems == null) {
      setFiles([...files(), ...nonEmpty]);
      setError(
        empty.length > 0 ? t('composer.errors.emptyFile', { names: empty.map((file) => file.name).join(', ') }) : null
      );
      return;
    }

    let accepted = nonEmpty;
    const oversized =
      limit == null
        ? []
        : nonEmpty.filter((file) => (passwordMode() === 'off' ? file.size : sealedSizeOf(file.size)) > limit);
    if (oversized.length > 0) {
      accepted = nonEmpty.filter((file) => !oversized.includes(file));
    }

    const skippedByCount: File[] = [];
    if (maxItems != null) {
      // The text block occupies one item slot when present.
      const remainingSlots = Math.max(0, maxItems - (hasText() ? 1 : 0) - files().length);
      if (accepted.length > remainingSlots) {
        skippedByCount.push(...accepted.slice(remainingSlots));
        accepted = accepted.slice(0, remainingSlots);
      }
    }

    setFiles([...files(), ...accepted]);
    if (empty.length > 0) {
      setError(
        t('composer.errors.emptyFile', {
          names: empty.map((file) => file.name).join(', ')
        })
      );
    } else if (oversized.length > 0 && limit != null) {
      setError(
        t('composer.errors.fileTooLarge', {
          names: oversized.map((file) => file.name).join(', '),
          limit: formatMb(limit)
        })
      );
    } else if (skippedByCount.length > 0 && maxItems != null) {
      setError(
        t('composer.errors.tooManyItemsSkipped', {
          names: skippedByCount.map((file) => file.name).join(', '),
          limit: maxItems
        })
      );
    } else {
      setError(null);
    }
  };

  const removeFile = (index: number) => {
    clearPartial();
    setError(null);
    setFiles((current) => current.filter((_, fileIndex) => fileIndex !== index));
  };

  const setPasswordMode = (mode: PasswordMode) => {
    clearPartial();
    setError(null);
    setPasswordModeBase(mode);
  };

  const setCustomPassphrase = (value: string) => {
    clearPartial();
    setError(null);
    setCustomPassphraseBase(value);
  };

  const regeneratePassphrase = () => {
    clearPartial();
    setGeneratedPassphrase(generatePassphrase());
  };

  const setExpiry = (index: number) => {
    clearPartial();
    setExpiryIndex(index);
  };

  const setDownloads = (index: number) => {
    clearPartial();
    setAccessIndex(index);
  };

  const buildItems = (): ComposerItem[] => {
    const items: ComposerItem[] = [];
    if (hasText()) {
      items.push({
        id: crypto.randomUUID(),
        kind: 'text',
        bytes: utf8(text().trim()),
        meta: { name: null, mime: 'text/plain' }
      });
    }
    for (const file of files()) {
      items.push({
        id: crypto.randomUUID(),
        kind: 'file',
        file,
        meta: { name: file.name, mime: file.type || 'application/octet-stream' }
      });
    }
    return items;
  };

  const itemName = (item: ComposerItem) =>
    item.kind === 'text' ? t('composer.textItemName') : (item.meta.name ?? t('composer.fileItemName'));

  const toPartial = (
    packageId: string,
    items: ComposerItem[],
    nextIndex: number,
    currentPassphrase: string | undefined,
    labels: { expiry: string; downloads: string; password: string },
    code?: string
  ): PartialPackage => ({
    packageId,
    items,
    nextIndex,
    passphrase: currentPassphrase,
    code,
    expiryLabel: labels.expiry,
    accessesLabel: labels.downloads,
    passwordLabel: labels.password
  });

  /**
   * Re-checks the items right before the first network call. The form can be
   * edited while limits load, and a resumed attempt may carry items that no
   * longer fit the current identity's limit.
   */
  const validateSubmit = (items: ComposerItem[], startIndex = 0): string | null => {
    if (items.length === 0) {
      return t('errors.missingContent');
    }

    // On resume, items before nextIndex are already stored. Re-checking them
    // would block the retry if the limit changed in the meantime.
    const pending = items.slice(startIndex);
    const empty = pending.filter((item) => item.kind === 'file' && item.file.size === 0).map(itemName);
    if (empty.length > 0) {
      return t('composer.errors.emptyFile', { names: empty.join(', ') });
    }

    const limit = fileLimitBytes();
    const maxItems = maxItemsPerPackage();
    if (limit == null && maxItems == null) {
      // Limits are advisory when they could not be loaded; the server enforces
      // them per item.
      return null;
    }

    if (limit != null) {
      const oversized = pending.filter((item) => storedSizeOf(item) > limit).map((item) => itemName(item));
      if (oversized.length > 0) {
        return t('composer.errors.fileTooLarge', { names: oversized.join(', '), limit: formatMb(limit) });
      }
    }

    // The item limit applies to the whole package, so the full item set is
    // checked here — including items already stored on a resumed attempt.
    if (maxItems != null && items.length > maxItems) {
      return t('composer.errors.tooManyItems', { limit: maxItems, count: items.length });
    }

    return null;
  };

  const runSubmit = async (resume?: PartialPackage) => {
    setSubmitting(true);
    setError(null);

    try {
      const items = resume?.items ?? buildItems();
      const isCodeMode = resume ? resume.code !== undefined : codeMode();
      const codeSecret = !resume && isCodeMode ? generateCodeSecret() : undefined;
      const currentPassphrase = resume ? resume.passphrase : isCodeMode ? codeSecret : passphrase();

      const validationError = validateSubmit(items, resume?.nextIndex ?? 0);
      if (validationError) {
        setProgress(null);
        setError(validationError);
        if (resume) {
          setPartial(resume);
        }
        return;
      }

      const labels = {
        expiry: resume?.expiryLabel ?? expiryLabel(),
        downloads: resume?.accessesLabel ?? accessesLabel(),
        password: resume?.passwordLabel ?? passwordLabel()
      };
      const packageId = resume?.packageId ?? crypto.randomUUID();

      let key: PackageKey | undefined;
      if (currentPassphrase) {
        key = await deriveKeyBits(currentPassphrase, await packageSalt(packageId));
      }

      let code = resume?.code;

      // Step 1: create the package unless we are resuming a failed attempt.
      if (!resume) {
        const verifier = key ? await unlockProof(key, packageId) : null;
        const created = await createPackage(
          {
            Id: packageId,
            UnlockVerifier: verifier,
            UseShortCode: isCodeMode,
            TimeToLiveHours: isCodeMode ? 1 : expiry().hours,
            MaxAccessCount: isCodeMode ? 1 : accessCount()
          },
          isCodeMode ? (verifier ?? undefined) : undefined
        );

        if (!created.data) {
          setProgress(null);
          setError(getErrorMessage(created.error, t('errors.createPackage')));
          return;
        }

        if (isCodeMode) {
          if (!created.data.ShortCode || !codeSecret) {
            setProgress(null);
            setError(t('errors.createCode'));
            return;
          }

          // The server owns the locator; the browser keeps the secret.
          code = `${created.data.ShortCode}${codeSecret}`;
          // The creating tab keeps the full code so the owner links list can
          // offer it until the ten-minute package lifetime ends.
          saveShortCode(packageId, code, created.data.ExpiresAt);
        }
      }

      // Step 2: seal content + metadata, then upload one item per request.
      abortController = new AbortController();

      for (let index = resume?.nextIndex ?? 0; index < items.length; index += 1) {
        const item = items[index];
        const fileName = itemName(item);
        const plainSize = item.kind === 'file' ? item.file.size : item.bytes.length;

        setProgress({
          phase: 'encrypting',
          index,
          total: items.length,
          fileName,
          sentBytes: 0,
          totalBytes: plainSize
        });

        let payload: Blob;
        let meta: string;
        try {
          // Unencrypted items are handed to the browser as they are: no
          // arrayBuffer copy and no second File wrapper. The browser streams
          // the original file straight from disk.
          const source =
            item.kind === 'file' ? item.file : new Blob([item.bytes], { type: 'application/octet-stream' });

          if (key) {
            // Text and files are encrypted slice-by-slice into 4 MiB frames;
            // a whole-file plaintext buffer is never allocated.
            // oxlint-disable-next-line no-await-in-loop -- items are prepared one at a time for progress
            payload = await encryptFile(await itemKey(key, item.id), source, (chunkIndex, isFinal) =>
              frameAad(packageId, item.id, 'item', chunkIndex, isFinal)
            );
          } else {
            payload = source;
          }

          // Sealing is deterministic, so every retry derives the same meta
          // blob — no caching is needed for idempotent retries.
          // oxlint-disable-next-line no-await-in-loop -- items are prepared one at a time for progress
          meta = toBase64Url(await encodeMeta(item.meta, key, packageId, item.id));
        } catch {
          setProgress(null);
          setError(t('errors.encrypt', { fileName }));
          setPartial(toPartial(packageId, items, index, currentPassphrase, labels, code));
          return;
        }

        setProgress({
          phase: 'uploading',
          index,
          total: items.length,
          fileName,
          sentBytes: 0,
          totalBytes: payload.size
        });

        let outcome: UploadItemResult;
        try {
          // oxlint-disable-next-line no-await-in-loop -- items are uploaded one at a time for progress/cancel
          outcome = await uploadItem(
            packageId,
            {
              Id: item.id,
              Kind: item.kind,
              Meta: meta,
              File: payload
            },
            (sentBytes, totalBytes) => {
              setProgress((current) => (current ? { ...current, phase: 'uploading', sentBytes, totalBytes } : current));
            },
            abortController.signal
          );
        } catch (err) {
          setProgress(null);
          setError(getErrorMessage(err, t('errors.upload', { fileName })));
          setPartial(toPartial(packageId, items, index, currentPassphrase, labels, code));
          return;
        }

        if (outcome.canceled) {
          setProgress(null);
          setError(t('errors.canceled'));
          setPartial(toPartial(packageId, items, index, currentPassphrase, labels, code));
          return;
        }

        if (!outcome.data) {
          setProgress(null);
          setError(getErrorMessage(outcome.error, t('errors.upload', { fileName })));
          setPartial(toPartial(packageId, items, index, currentPassphrase, labels, code));
          return;
        }
      }

      setProgress(null);
      abortController = null;

      const link = `${window.location.origin}/s/${packageId}`;
      setResult({
        link,
        code,
        linkWithPassphrase:
          currentPassphrase && !isCodeMode ? `${link}#s=${encodeURIComponent(currentPassphrase)}` : undefined,
        passphrase: isCodeMode ? undefined : currentPassphrase,
        expiryLabel: labels.expiry,
        accessesLabel: labels.downloads,
        passwordLabel: labels.password
      });
      setStage('result');
    } catch (err) {
      setProgress(null);
      setError(getErrorMessage(err, t('errors.createPackage')));
    } finally {
      setSubmitting(false);
    }
  };

  const handleSubmit = (event?: Event) => {
    event?.preventDefault();
    if (!canSubmit()) {
      return;
    }

    const resume = partial();
    setPartial(null);
    setResult(null);
    void runSubmit(resume ?? undefined);
  };

  const handleCancel = () => {
    abortController?.abort();
  };

  const resetForm = () => {
    clearPartial();
    abortController = null;
    setTextBase('');
    setFiles([]);
    setOptionsOpen(false);
    setPasswordModeBase(DEFAULT_PASSWORD_MODE);
    setCustomPassphraseBase('');
    setGeneratedPassphrase(generatePassphrase());
    setExpiryIndex(DEFAULT_EXPIRY_INDEX);
    setAccessIndex(DEFAULT_ACCESS_COUNT_INDEX);
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
      return t('composer.progress.encrypting', {
        current: current.index + 1,
        total: current.total,
        fileName: current.fileName
      });
    }
    const percent = current.totalBytes > 0 ? Math.round((current.sentBytes / current.totalBytes) * 100) : 0;
    return t('composer.progress.uploading', {
      current: current.index + 1,
      total: current.total,
      percent
    });
  });

  const totalSize = createMemo(() => files().reduce((sum, file) => sum + file.size, 0));

  return {
    stage,
    text,
    files,
    hasFiles,
    hasContent,
    optionsOpen,
    summary,
    passwordMode,
    customPassphrase,
    generatedPassphrase,
    passphraseCheck,
    passphraseError,
    isAuthenticated,
    expiryIndex,
    accessIndex,
    expiryLabel,
    accessesLabel,
    passwordLabel,
    limitLabel,
    totalSize,
    submitting,
    progress,
    progressLabel,
    error,
    partial,
    result,
    canSubmit,
    setText,
    addFiles,
    removeFile,
    setOptionsOpen,
    setPasswordMode,
    setCustomPassphrase,
    regeneratePassphrase,
    setExpiry,
    setDownloads,
    handleSubmit,
    handleCancel,
    resetForm
  };
}
