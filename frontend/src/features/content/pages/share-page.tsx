import { For, Show, createEffect, createSignal } from 'solid-js';
import { useParams } from '@solidjs/router';
import type { ContentItemInPackage, ContentPackageDetailsResponse } from '../../../client/model/index.ts';
import { getPackageDetails, downloadContent } from '../../../shared/api/content.ts';
import { getErrorMessage } from '../../../shared/api/error-message.ts';
import { decryptFile, decryptText, isEncryptedBlob } from '../../../shared/crypto/crypto.ts';
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

  const downloadFailed = (item: ContentItemInPackage, message: string) => {
    setDownloadErrors((current) => ({ ...current, [item.Id]: message }));
  };

  const downloadCleared = (item: ContentItemInPackage) => {
    setDownloadErrors((current) => ({ ...current, [item.Id]: '' }));
  };

  // Downloads the item's blob and decrypts it when the content is encrypted,
  // returning either the ready-to-save blob or a user-facing error message.
  const prepareDownload = async (item: ContentItemInPackage): Promise<Blob | string> => {
    if (!item.DownloadToken) {
      return ''; // the card already explains the missing token
    }

    let blob: Blob;
    try {
      const result = await downloadContent(packageId(), item.Id, item.DownloadToken);
      if (!result.data) {
        return getErrorMessage(result.error, t('share.downloadFailed'));
      }
      blob = result.data;
    } catch (err) {
      return getErrorMessage(err, t('share.downloadFailed'));
    }

    const passphraseError = () => (passphrase() ? '' : t('share.enterPassphrase'));

    if (await isEncryptedBlob(blob)) {
      const error = passphraseError();
      if (error) {
        return error;
      }
      try {
        return await decryptFile(blob, passphrase());
      } catch {
        return t('share.invalidPassphrase');
      }
    }

    if (item.Type === 'text') {
      // Oversized text items are delivered as blobs; an encrypted envelope
      // must be decrypted with the passphrase before saving.
      const text = await blob.text();
      if (parseEncryptedTextEnvelope(text)) {
        const error = passphraseError();
        if (error) {
          return error;
        }
        try {
          return new Blob([await decryptText(text, passphrase())], { type: 'text/plain;charset=utf-8' });
        } catch {
          return t('share.invalidPassphrase');
        }
      }
    }

    return blob;
  };

  const downloadItem = async (item: ContentItemInPackage) => {
    const outcome = await prepareDownload(item);
    if (typeof outcome === 'string') {
      if (outcome) {
        downloadFailed(item, outcome);
      }
      return;
    }
    downloadCleared(item);
    saveBlob(item, outcome);
  };

  return (
    <div class="grid gap-4 lg:grid-cols-[minmax(0,18rem)_minmax(0,1fr)] lg:items-start lg:gap-6">
      <section class="lg:sticky lg:top-28">
        <SharePackageSummary
          loading={loading()}
          loadError={loadError() ? getErrorMessage(loadError(), t('share.loadFailed')) : undefined}
          details={details()}
        />
      </section>

      <Show when={details()?.Items?.length}>
        <div class="grid gap-3">
          <For each={details()?.Items ?? []}>
            {(item) => (
              <ShareItemCard
                item={item}
                passphraseValue={passphrase()}
                onPassphraseInput={setPassphrase}
                onDecrypt={() => void decryptItem(item)}
                isDecrypting={decrypting()[item.Id] ?? false}
                decryptedText={decrypted()[item.Id]}
                decryptError={errors()[item.Id]}
                onDownload={() => void downloadItem(item)}
                downloadError={downloadErrors()[item.Id]}
              />
            )}
          </For>
        </div>
      </Show>
    </div>
  );
}
