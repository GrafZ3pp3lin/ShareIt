import { createMemo, createSignal, onSettled } from 'solid-js';
import { createPackage, getUploadLimits, uploadItem } from '../../shared/api/content.ts';
import type { UploadLimits } from '../../shared/api/content.ts';
import { getErrorMessage } from '../../shared/api/error-message.ts';
import { toBase64Url } from '../../shared/crypto/base64url.ts';
import { utf8 } from '../../shared/crypto/bytes.ts';
import {
  deriveKeyBits,
  itemAad,
  itemKey,
  packageSalt,
  unlockProof,
  type PackageKey
} from '../../shared/crypto/keys.ts';
import { encodeMeta, type ItemMeta } from '../../shared/crypto/meta.ts';
import { generatePassphrase, validatePassphrase } from '../../shared/crypto/passphrase.ts';
import { seal, SEALED_OVERHEAD_BYTES } from '../../shared/crypto/sealed.ts';
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

export type PasswordMode = 'off' | 'generate' | 'custom';

export type ComposerResult = {
  link: string;
  linkWithPassphrase?: string;
  passphrase?: string;
  expiryLabel: string;
  accessesLabel: string;
  passwordLabel: string;
  encrypted: boolean;
  /** True when the result panel shows a link whose item uploads did not all finish. */
  incomplete?: boolean;
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
  | { id: string; kind: 'text'; bytes: Uint8Array<ArrayBuffer>; meta: ItemMeta; sealedMeta?: string }
  | { id: string; kind: 'file'; file: File; meta: ItemMeta; sealedMeta?: string };

type PartialPackage = {
  packageId: string;
  items: ComposerItem[];
  nextIndex: number;
  passphrase: string | undefined;
  expiryLabel: string;
  accessesLabel: string;
  passwordLabel: string;
  encrypted: boolean;
};

const formatMb = (bytes: number) => `${Math.round(bytes / (1024 * 1024))} MB`;

/**
 * Owns the composer form state and the upload pipeline: generate ids, derive
 * the package key (when a passphrase is set), seal every item and its metadata,
 * create the package, then upload one item per request with progress.
 *
 * Ids are generated once per attempt and kept in the partial state, so a retry
 * reuses the same package and item ids (the backend treats both as idempotent).
 */
export function createComposer() {
  const { t } = useI18n();
  const { isAuthenticated } = useAuth();

  const [stage, setStage] = createSignal<'form' | 'result'>('form');
  const [text, setTextBase] = createSignal('');
  const [files, setFiles] = createSignal<File[]>([]);
  const [optionsOpen, setOptionsOpen] = createSignal(false);
  const [passwordMode, setPasswordModeBase] = createSignal<PasswordMode>('off');
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

  // Guests have no pickers: they always get the shortest lifetime and one access.
  const expiry = createMemo(() => EXPIRY_OPTIONS[isAuthenticated() ? expiryIndex() : ANONYMOUS_EXPIRY_INDEX]);
  const accessCount = createMemo(() =>
    isAuthenticated() ? ACCESS_COUNT_OPTIONS[accessIndex()] : ANONYMOUS_ACCESS_COUNT
  );

  const expiryLabel = createMemo(() => t(expiry().labelKey));
  const accessesLabel = createMemo(() => {
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
    return mode === 'custom' ? t('options.password.custom') : t('options.password.off');
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

  const limitLabel = createMemo(() => {
    const limit = fileLimitBytes();
    return limit == null ? undefined : t('composer.limit', { limit: formatMb(limit) });
  });

  const canSubmit = createMemo(() => hasContent() && !submitting() && passphraseError() === null);

  const fileBytesLeft = createMemo(() => {
    const limit = fileLimitBytes();
    if (limit == null) {
      return null;
    }
    // Sealed items carry the fixed 32-byte SIE2 overhead.
    return passwordMode() === 'off' ? limit : limit - SEALED_OVERHEAD_BYTES;
  });

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

    const limit = fileBytesLeft();
    if (limit == null) {
      setFiles([...files(), ...unique]);
      setError(null);
      return;
    }

    const oversized = unique.filter((file) => file.size > limit);
    setFiles([...files(), ...unique.filter((file) => file.size <= limit)]);
    setError(
      oversized.length > 0
        ? t('composer.errors.fileTooLarge', {
            names: oversized.map((file) => file.name).join(', '),
            limit: formatMb(limit)
          })
        : null
    );
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
    labels: { expiry: string; downloads: string; password: string }
  ): PartialPackage => ({
    packageId,
    items,
    nextIndex,
    passphrase: currentPassphrase,
    expiryLabel: labels.expiry,
    accessesLabel: labels.downloads,
    passwordLabel: labels.password,
    encrypted: Boolean(currentPassphrase)
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
    const limit = fileBytesLeft();
    if (limit == null) {
      // Limits are advisory when they could not be loaded; the server enforces
      // them and reports a 413 per item.
      return null;
    }

    const oversized = pending
      .filter((item) => (item.kind === 'file' ? item.file.size : item.bytes.length) > limit)
      .map((item) => itemName(item));

    return oversized.length > 0
      ? t('composer.errors.fileTooLarge', { names: oversized.join(', '), limit: formatMb(limit) })
      : null;
  };

  const runSubmit = async (resume?: PartialPackage) => {
    setSubmitting(true);
    setError(null);

    try {
      const items = resume?.items ?? buildItems();
      const currentPassphrase = resume ? resume.passphrase : passphrase();

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

      // Step 1: create the package unless we are resuming a failed attempt.
      if (!resume) {
        const verifier = key ? await unlockProof(key, packageId) : null;
        const created = await createPackage({
          Id: packageId,
          UnlockVerifier: verifier,
          TimeToLiveHours: expiry().hours,
          MaxAccessCount: accessCount()
        });

        if (!created.data) {
          setProgress(null);
          setError(getErrorMessage(created.error, t('errors.createPackage')));
          return;
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
          if (key) {
            // oxlint-disable-next-line no-await-in-loop -- items are prepared one at a time for progress
            const plain = item.kind === 'file' ? new Uint8Array(await item.file.arrayBuffer()) : item.bytes;
            // oxlint-disable-next-line no-await-in-loop -- items are prepared one at a time for progress
            const sealed = await seal(await itemKey(key, item.id), plain, itemAad(packageId, item.id));
            payload = new Blob([sealed], { type: 'application/octet-stream' });
          } else {
            // Unencrypted items are handed to the browser as they are: no
            // arrayBuffer copy and no second File wrapper. The browser streams
            // the original file straight from disk.
            payload = item.kind === 'file' ? item.file : new Blob([item.bytes], { type: 'application/octet-stream' });
          }

          if (item.sealedMeta) {
            // Reuse the exact sealed meta across retries. It contains a random
            // IV, and the server uses it as part of the idempotency fingerprint.
            meta = item.sealedMeta;
          } else {
            // oxlint-disable-next-line no-await-in-loop -- items are prepared one at a time for progress
            meta = toBase64Url(await encodeMeta(item.meta, key, packageId, item.id));
            item.sealedMeta = meta;
          }
        } catch {
          setProgress(null);
          setError(t('errors.encrypt', { fileName }));
          setPartial(toPartial(packageId, items, index, currentPassphrase, labels));
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

        // oxlint-disable-next-line no-await-in-loop -- items are uploaded one at a time for progress/cancel
        const outcome = await uploadItem(
          packageId,
          {
            Id: item.id,
            Kind: item.kind,
            Meta: meta,
            File: payload,
            FileName: item.kind === 'text' ? 'content.txt' : fileName
          },
          (sentBytes, totalBytes) => {
            setProgress((current) => (current ? { ...current, phase: 'uploading', sentBytes, totalBytes } : current));
          },
          abortController.signal
        );

        if (outcome.canceled) {
          setProgress(null);
          setError(t('errors.canceled'));
          setPartial(toPartial(packageId, items, index, currentPassphrase, labels));
          return;
        }

        if (!outcome.data) {
          setProgress(null);
          setError(getErrorMessage(outcome.error, t('errors.upload', { fileName })));
          setPartial(toPartial(packageId, items, index, currentPassphrase, labels));
          return;
        }
      }

      setProgress(null);
      abortController = null;

      const link = `${window.location.origin}/s/${packageId}`;
      setResult({
        link,
        linkWithPassphrase: currentPassphrase ? `${link}#s=${encodeURIComponent(currentPassphrase)}` : undefined,
        passphrase: currentPassphrase,
        expiryLabel: labels.expiry,
        accessesLabel: labels.downloads,
        passwordLabel: labels.password,
        encrypted: Boolean(currentPassphrase)
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

  const showLinkAnyway = () => {
    const resume = partial();
    if (!resume) {
      return;
    }

    setPartial(null);
    setError(null);
    setResult({
      link: `${window.location.origin}/s/${resume.packageId}`,
      linkWithPassphrase: resume.passphrase
        ? `${window.location.origin}/s/${resume.packageId}#s=${encodeURIComponent(resume.passphrase)}`
        : undefined,
      passphrase: resume.passphrase,
      expiryLabel: resume.expiryLabel,
      accessesLabel: resume.accessesLabel,
      passwordLabel: resume.passwordLabel,
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
    abortController = null;
    setTextBase('');
    setFiles([]);
    setOptionsOpen(false);
    setPasswordModeBase('off');
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
    showLinkAnyway,
    resetForm
  };
}
