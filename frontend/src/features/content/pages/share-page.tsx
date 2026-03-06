import { useQuery } from '@tanstack/solid-query';
import { useParams } from '@tanstack/solid-router';
import { For, Show, createSignal } from 'solid-js';
import type { ContentItemInPackage } from '../../../client';
import { getPackageDetails, downloadContent } from '../../../shared/api/content';
import { getErrorMessage } from '../../../shared/api/error-message';
import { decryptText } from '../../../shared/crypto/crypto';
import { parseEncryptedTextEnvelope } from '../../../shared/crypto/envelope';
import { useI18n } from '../../../i18n';
import { ShareItemCard } from '../components/share-item-card';
import { SharePackageSummary } from '../components/share-package-summary';

export function SharePage() {
  const { t } = useI18n();
  const params = useParams({ strict: false });
  const packageId = () => params().packageId ?? '';
  const [passphrases, setPassphrases] = createSignal<Record<string, string>>({});
  const [decrypted, setDecrypted] = createSignal<Record<string, string>>({});
  const [decrypting, setDecrypting] = createSignal<Record<string, boolean>>({});
  const [errors, setErrors] = createSignal<Record<string, string>>({});
  const [downloadErrors, setDownloadErrors] = createSignal<Record<string, string>>({});

  const packageQuery = useQuery(() => ({
    queryKey: ['package', packageId()],
    queryFn: () => getPackageDetails(packageId()),
    enabled: Boolean(packageId()),
  }));

  const packageData = () => packageQuery.data?.data;
  const packageError = () => packageQuery.data?.error;

  const setPassphrase = (id: string, value: string) => {
    setPassphrases((current) => ({ ...current, [id]: value }));
  };

  const decryptItem = async (item: ContentItemInPackage) => {
    const passphrase = passphrases()[item.Id];
    if (!passphrase) {
      setErrors((current) => ({
        ...current,
        [item.Id]: t('share.enterPassphrase'),
      }));
      return;
    }
    setDecrypting((current) => ({ ...current, [item.Id]: true }));
    setErrors((current) => ({ ...current, [item.Id]: '' }));
    try {
      const plain = await decryptText(item.Text ?? '', passphrase);
      setDecrypted((current) => ({ ...current, [item.Id]: plain }));
    } catch {
      setErrors((current) => ({
        ...current,
        [item.Id]: t('share.invalidPassphrase'),
      }));
    } finally {
      setDecrypting((current) => ({ ...current, [item.Id]: false }));
    }
  };

  const downloadItem = async (item: ContentItemInPackage) => {
    if (!item.DownloadToken) {
      return;
    }
    const result = await downloadContent(packageId(), item.Id, item.DownloadToken);
    if (!result.data) {
      setDownloadErrors((current) => ({
        ...current,
        [item.Id]: getErrorMessage(result.error, t('share.downloadFailed')),
      }));
      return;
    }

    setDownloadErrors((current) => ({ ...current, [item.Id]: '' }));
    const url = URL.createObjectURL(result.data);
    const link = document.createElement('a');
    link.href = url;
    link.download = item.FileName ?? t('share.defaultFileName');
    link.click();
    URL.revokeObjectURL(url);
  };

  return (
    <div class="grid gap-4 lg:grid-cols-[minmax(0,18rem)_minmax(0,1fr)] lg:items-start lg:gap-6">
      <section class="lg:sticky lg:top-28">
        <SharePackageSummary
          title={t('share.title')}
          loadingLabel={packageQuery.isLoading ? t('share.loading') : ''}
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
              const decryptedText = () => decrypted()[item.Id];
              const errorMessage = () => errors()[item.Id];
              const downloadError = () => downloadErrors()[item.Id];

              return (
                <ShareItemCard
                  item={item}
                  title={item.FileName ?? (item.Type === 'Text' ? t('share.textEntry') : t('share.content'))}
                  encryptedNoticeLabel={t('share.encryptedNotice')}
                  passphrasePlaceholder={t('share.passphrasePlaceholder')}
                  decryptLabel={t('share.decrypt')}
                  decryptingLabel={t('share.decrypting')}
                  decryptError={errorMessage()}
                  decryptedText={decryptedText()}
                  rawText={item.Text ?? ''}
                  mimeType={item.MimeType}
                  canDownload={Boolean(item.DownloadToken)}
                  downloadLabel={t('share.downloadFile')}
                  onDownload={() => void downloadItem(item)}
                  downloadError={downloadError()}
                  noDownloadTokenLabel={t('share.noDownloadToken')}
                  isEncryptedText={Boolean(envelope)}
                  passphraseValue={passphrases()[item.Id] ?? ''}
                  onPassphraseInput={(value) => setPassphrase(item.Id, value)}
                  onDecrypt={() => void decryptItem(item)}
                  isDecrypting={Boolean(decrypting()[item.Id])}
                />
              );
            }}
          </For>
        </div>
      </Show>
    </div>
  );
}
