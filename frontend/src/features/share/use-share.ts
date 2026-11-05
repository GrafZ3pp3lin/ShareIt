import { createEffect, createSignal } from 'solid-js';
import type { ContentItemInPackage, ContentPackageDetailsResponse } from '../../client/model/index.ts';
import { useI18n } from '../../i18n/i18n.tsx';
import { getPackageDetails } from '../../shared/api/content.ts';
import { getErrorMessage } from '../../shared/api/error-message.ts';
import { fromBase64Url } from '../../shared/crypto/base64url.ts';
import { deriveKeyBits, packageSalt, unlockProof, type PackageKey } from '../../shared/crypto/keys.ts';
import { decodeMeta, type ItemMeta } from '../../shared/crypto/meta.ts';

export type ShareStatus = 'locked' | 'unlocking' | 'unlocked' | 'invalid' | 'gone';

export type ShareItem = {
  Id: string;
  Kind: string;
  Size: number;
  Meta?: string | null;
  Payload?: string | null;
  DownloadToken?: string | null;
  index: number;
  decodedMeta: ItemMeta | null;
  displayName: string;
  mime: string;
};

export type GateMessage = {
  title: string;
  body: string;
};

const isTextKind = (kind: string) => kind === 'text';

export const isTextItem = (item: ShareItem) => isTextKind(item.Kind);

const fragmentPassphrase = (): string | undefined => {
  if (!window.location.hash) {
    return undefined;
  }

  try {
    const value = new URLSearchParams(window.location.hash.slice(1)).get('s');
    return value ? value : undefined;
  } catch {
    return undefined;
  }
};

const dropFragment = () => {
  if (!window.location.hash) {
    return;
  }
  window.history.replaceState(null, '', `${window.location.pathname}${window.location.search}`);
};

/**
 * Owns the share-page state machine. A link without `#s=` starts locked and
 * makes no request until the visitor submits the gate; a link with `#s=`
 * derives the proof and fetches immediately, then removes the fragment.
 */
export function createShare(packageId: () => string) {
  const { t } = useI18n();

  // A combined link starts in the unlocking state so the gate never flashes
  // between the first render and the effect that starts the fragment attempt.
  const initialFragment = fragmentPassphrase();
  const [status, setStatus] = createSignal<ShareStatus>(initialFragment !== undefined ? 'unlocking' : 'locked');
  const [details, setDetails] = createSignal<ContentPackageDetailsResponse | null>(null);
  const [items, setItems] = createSignal<ShareItem[]>([]);
  const [packageKey, setPackageKey] = createSignal<PackageKey | undefined>(undefined);
  const [passphrase, setPassphrase] = createSignal(initialFragment ?? '');
  const [gateMessage, setGateMessage] = createSignal<GateMessage | null>(null);

  let attempt = 0;

  const decodeItems = async (
    rawItems: ContentItemInPackage[],
    key: PackageKey | undefined,
    id: string
  ): Promise<ShareItem[]> =>
    Promise.all(
      rawItems.map(async (item, index) => {
        let decodedMeta: ItemMeta | null = null;
        if (item.Meta) {
          try {
            decodedMeta = await decodeMeta(fromBase64Url(item.Meta), key, id, item.Id);
          } catch {
            decodedMeta = null;
          }
        }

        const fallbackName = t('share.fileFallback', { index: index + 1 });
        return {
          Id: item.Id,
          Kind: item.Kind,
          Size: Number(item.Size),
          Meta: item.Meta,
          Payload: item.Payload,
          DownloadToken: item.DownloadToken,
          index,
          decodedMeta,
          displayName: decodedMeta?.name ?? fallbackName,
          mime: decodedMeta?.mime ?? (isTextKind(item.Kind) ? 'text/plain' : 'application/octet-stream')
        };
      })
    );

  const failUnlock = (httpStatus: number | undefined, attemptedPassphrase: boolean, error: unknown) => {
    if (httpStatus === 401) {
      setStatus('invalid');
      setGateMessage(
        attemptedPassphrase
          ? { title: t('gate.invalidTitle'), body: t('gate.invalidBody') }
          : { title: t('gate.missingTitle'), body: t('gate.missingBody') }
      );
      return;
    }

    if (httpStatus === 400 || httpStatus === 404 || httpStatus === 410) {
      setStatus('gone');
      setGateMessage(null);
      return;
    }

    if (httpStatus === 429) {
      setStatus('locked');
      setGateMessage({ title: t('gate.rateLimitedTitle'), body: t('gate.rateLimitedBody') });
      return;
    }

    setStatus('locked');
    setGateMessage({
      title: t('gate.failedTitle'),
      body: getErrorMessage(error, t('gate.failedBody'))
    });
  };

  const unlockWith = async (value: string) => {
    const id = packageId();
    const current = ++attempt;

    if (!id) {
      setStatus('gone');
      return;
    }

    setStatus('unlocking');
    setGateMessage(null);
    setDetails(null);
    setItems([]);
    setPackageKey(undefined);

    let key: PackageKey | undefined;
    let proof: string | undefined;
    if (value !== '') {
      try {
        key = await deriveKeyBits(value, await packageSalt(id));
        proof = await unlockProof(key, id);
      } catch {
        if (current === attempt) {
          setStatus('gone');
        }
        return;
      }
    }

    try {
      const outcome = await getPackageDetails(id, proof);
      if (current !== attempt) {
        return;
      }

      if (!outcome.data) {
        failUnlock(outcome.status, value !== '', outcome.error);
        return;
      }

      const decodedItems = await decodeItems(outcome.data.Items, key, id);
      if (current !== attempt) {
        return;
      }

      setPackageKey(key);
      setDetails(outcome.data);
      setItems(decodedItems);
      setStatus('unlocked');
      dropFragment();
    } catch (error) {
      if (current !== attempt) {
        return;
      }
      setStatus('locked');
      setGateMessage({
        title: t('gate.failedTitle'),
        body: getErrorMessage(error, t('gate.failedBody'))
      });
    }
  };

  const start = () => {
    // Cancel any request from the previous package id before starting over.
    attempt += 1;
    setDetails(null);
    setItems([]);
    setPackageKey(undefined);
    setGateMessage(null);
    setPassphrase('');

    const fragment = fragmentPassphrase();
    if (fragment !== undefined) {
      setPassphrase(fragment);
      void unlockWith(fragment);
      return;
    }

    setStatus('locked');
  };

  createEffect(() => packageId(), start);

  const unlock = () => {
    void unlockWith(passphrase());
  };

  return {
    packageId,
    status,
    details,
    items,
    packageKey,
    passphrase,
    setPassphrase,
    gateMessage,
    unlock
  };
}
