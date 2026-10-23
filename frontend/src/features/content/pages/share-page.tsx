import { For, Show, createEffect, createSignal } from 'solid-js';
import { useParams } from '@solidjs/router';
import type { ContentItemInPackage, ContentPackageDetailsResponse } from '../../../client/model/index.ts';
import { getPackageDetails, downloadContent } from '../../../shared/api/content.ts';
import type { ApiResult } from '../../../shared/api/content.ts';
import { getErrorMessage } from '../../../shared/api/error-message.ts';
import { decryptFile, decryptText, isEncryptedBlob, ENCRYPTED_FILE_MIME_TYPE } from '../../../shared/crypto/crypto.ts';
import { parseEncryptedTextEnvelope } from '../../../shared/crypto/envelope.ts';
import { ShareItemCard } from '../components/share-item-card.tsx';
import { useI18n } from '../../../i18n/i18n.tsx';
import { SharePackageSummary } from '../components/share-package-summary.tsx';

export function SharePage() {
  const { t } = useI18n();
  const params = useParams();
  const packageId = () => params.packageId ?? '';
  // One passphrase protects the whole package (texts and files alike).
  const [passphrase, setPassphrase] = createSignal('');
  const [decrypted, setDecrypted] = createSignal<Record<string, string>>({});
  const [decrypting, setDecrypting] = createSignal<Record<string, boolean>>({});
  const [errors, setErrors] = createSignal<Record<string, string>>({});
  const [downloadErrors, setDownloadErrors] = createSignal<Record<string, string>>({});

  // Explicit one-shot load per address: opening the link consumes one of the
  // package's accesses, so the result is never refetched in the background
  // (window focus, remounts, etc.) — the access would burn without the user
  // actually reading the content.
  const [details, setDetails] = createSignal<ContentPackageDetailsResponse | null>(null);
  const [loading, setLoading] = createSignal(true);
  const [loadError, setLoadError] = createSignal<unknown>(null);

  const loadDetails = async (id: string) => {
    setLoading(true);
    setLoadError(null);
    setDetails(null);
    try {
      const result = await getPackageDetails(id);
      if (result.data) {
        setDetails(result.data);
      } else {
        setLoadError(result.error);
      }
    } catch (err) {
      setLoadError(err);
    } finally {
      setLoading(false);
    }
  };

  createEffect(
    () => packageId(),
    (id) => {
      void loadDetails(id);
    }
  );

  const packageData = () => details();
  const packageError = () => loadError();

  const decryptItem = async (item: ContentItemInPackage) => {
    const value = passphrase();
    if (!value) {
      setErrors((current) => ({
        ...current,
        [item.Id]: t('share.enterPassphrase')
      }));
      return;
    }
    setDecrypting((current) => ({ ...current, [item.Id]: true }));
    setErrors((current) => ({ ...current, [item.Id]: '' }));
    try {
      const plain = await decryptText(item.Text ?? '', value);
      setDecrypted((current) => ({ ...current, [item.Id]: plain }));
    } catch {
      setErrors((current) => ({
        ...current,
        [item.Id]: t('share.invalidPassphrase')
      }));
    } finally {
      setDecrypting((current) => ({ ...current, [item.Id]: false }));
    }
  };

  const saveBlob = (item: ContentItemInPackage, blob: Blob) => {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = item.FileName ?? t('share.defaultFileName');
    link.click();
    URL.revokeObjectURL(url);
  };

  const downloadItem = async (item: ContentItemInPackage) => {
    if (!item.DownloadToken) {
      return;
    }
    let result: ApiResult<Blob>;
    try {
      result = await downloadContent(packageId(), item.Id, item.DownloadToken);
    } catch (err) {
      setDownloadErrors((current) => ({
        ...current,
        [item.Id]: getErrorMessage(err, t('share.downloadFailed'))
      }));
      return;
    }
    if (!result.data) {
      setDownloadErrors((current) => ({
        ...current,
        [item.Id]: getErrorMessage(result.error, t('share.downloadFailed'))
      }));
      return;
    }

    setDownloadErrors((current) => ({ ...current, [item.Id]: '' }));

    let blob: Blob = result.data;
    if (await isEncryptedBlob(blob)) {
      const value = passphrase();
      if (!value) {
        setDownloadErrors((current) => ({
          ...current,
          [item.Id]: t('share.enterPassphrase')
        }));
        return;
      }
      try {
        blob = await decryptFile(blob, value);
      } catch {
        setDownloadErrors((current) => ({
          ...current,
          [item.Id]: t('share.invalidPassphrase')
        }));
        return;
      }
    } else if (item.Type === 'text') {
      // Oversized text items are delivered as blobs; an encrypted envelope
      // must be decrypted with the passphrase before saving.
      const text = await blob.text();
      if (parseEncryptedTextEnvelope(text)) {
        const value = passphrase();
        if (!value) {
          setDownloadErrors((current) => ({
            ...current,
            [item.Id]: t('share.enterPassphrase')
          }));
          return;
        }
        try {
          blob = new Blob([await decryptText(text, value)], { type: 'text/plain;charset=utf-8' });
        } catch {
          setDownloadErrors((current) => ({
            ...current,
            [item.Id]: t('share.invalidPassphrase')
          }));
          return;
        }
      }
    }

    saveBlob(item, blob);
  };

  return (
    <div class="grid gap-4 lg:grid-cols-[minmax(0,18rem)_minmax(0,1fr)] lg:items-start lg:gap-6">
      <section class="lg:sticky lg:top-28">
        <SharePackageSummary
          title={t('share.title')}
          loadingLabel={loading() ? t('share.loading') : ''}
          loadError={packageError() ? getErrorMessage(packageError(), t('share.loadFailed')) : undefined}
          packageLabel={packageData() ? t('share.packageLabel', { id: packageData()!.Package.Id }) : undefined}
          itemsLabel={packageData() ? t('share.itemsLabel', { count: packageData()!.Package.ItemCount }) : undefined}
        />
      </section>

      <Show when={packageData()?.Items?.length}>
        <div class="grid gap-3">
          <For each={packageData()?.Items ?? []}>
            {(item) => {
              const envelope = item.Text ? parseEncryptedTextEnvelope(item.Text) : null;
              const isEncryptedFile = item.MimeType === ENCRYPTED_FILE_MIME_TYPE;
              const decryptedText = () => decrypted()[item.Id];
              const errorMessage = () => errors()[item.Id];
              const downloadError = () => downloadErrors()[item.Id];

              return (
                <ShareItemCard
                  item={item}
                  title={item.FileName ?? (item.Type === 'text' ? t('share.textEntry') : t('share.content'))}
                  encryptedNoticeLabel={t('share.encryptedNotice')}
                  encryptedFileNoticeLabel={t('share.encryptedFileNotice')}
                  passphrasePlaceholder={t('share.passphrasePlaceholder')}
                  decryptLabel={t('share.decrypt')}
                  decryptingLabel={t('share.decrypting')}
                  decryptError={errorMessage()}
                  decryptedText={decryptedText()}
                  rawText={item.Text ?? ''}
                  mimeType={isEncryptedFile ? null : item.MimeType}
                  canDownload={Boolean(item.DownloadToken)}
                  downloadLabel={t('share.downloadFile')}
                  onDownload={() => void downloadItem(item)}
                  downloadError={downloadError()}
                  noDownloadTokenLabel={t('share.noDownloadToken')}
                  isEncryptedText={Boolean(envelope)}
                  isEncryptedFile={isEncryptedFile}
                  passphraseValue={passphrase()}
                  onPassphraseInput={setPassphrase}
                  onDecrypt={() => void decryptItem(item)}
                  isDecrypting={decrypting()[item.Id] ?? false}
                />
              );
            }}
          </For>
        </div>
      </Show>
    </div>
  );
}
