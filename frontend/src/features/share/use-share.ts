import { createEffect, createMemo, createSignal } from 'solid-js';
import type { ContentItemInPackage, ContentPackageDetailsResponse } from '../../client/model/index.ts';
import { useI18n } from '../../i18n/i18n.tsx';
import { getPackageDetails } from '../../shared/api/content.ts';
import { getErrorMessage } from '../../shared/api/error-message.ts';
import { fromBase64Url } from '../../shared/crypto/base64url.ts';
import { deriveKeyBits, packageSalt, unlockProof, type PackageKey } from '../../shared/crypto/keys.ts';
import { decodeMeta, type ItemMeta } from '../../shared/crypto/meta.ts';
import { clearAccessSession, loadAccessSession, saveAccessSession } from './access-session.ts';

export type ShareStatus = 'locked' | 'unlocking' | 'unlocked' | 'invalid' | 'gone';

export type ShareItem = {
  Id: string;
  Kind: string;
  Size: number;
  Meta?: string | null;
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
 * Owns the share-page state machine. A link without `#s=` and without a code
 * secret starts locked and makes no request until the visitor submits the
 * gate; a combined link or a code entry derives the proof and fetches
 * immediately, then removes the fragment.
 */
export function createShare(packageId: () => string, initialSecret: () => string | undefined = () => undefined) {
  const { t } = useI18n();

  // Null until the start effect has run. Until then a combined link or a code
  // entry reads as unlocking, so the gate never flashes before the attempt.
  const [startedStatus, setStatus] = createSignal<ShareStatus | null>(null);
  const status = (): ShareStatus =>
    startedStatus() ?? ((initialSecret() ?? fragmentPassphrase()) !== undefined ? 'unlocking' : 'locked');
  const [details, setDetails] = createSignal<ContentPackageDetailsResponse | null>(null);
  const [items, setItems] = createSignal<ShareItem[]>([]);
  const [packageKey, setPackageKey] = createSignal<PackageKey | undefined>(undefined);
  const [accessSessionToken, setAccessSessionToken] = createSignal<string | undefined>(undefined);
  const [passphrase, setPassphrase] = createSignal('');
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
          index,
          decodedMeta,
          displayName: decodedMeta?.name ?? fallbackName,
          mime: decodedMeta?.mime ?? (isTextKind(item.Kind) ? 'text/plain' : 'application/octet-stream')
        };
      })
    );

  const failUnlock = (id: string, httpStatus: number | undefined, attemptedPassphrase: boolean, error: unknown) => {
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
      clearAccessSession(id);
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

  const unlockWith = async (id: string, value: string) => {
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
    setAccessSessionToken(undefined);

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

    const storedSession = loadAccessSession(id);

    try {
      const outcome = await getPackageDetails(id, proof, storedSession?.Token);
      if (current !== attempt) {
        return;
      }

      if (!outcome.data) {
        failUnlock(id, outcome.status, value !== '', outcome.error);
        return;
      }

      // Persist the grant before decoding: a decode failure must not lose the
      // access that was just claimed.
      saveAccessSession(id, outcome.data.AccessSession);

      // The server decides protection. A passphrase typed into an unprotected
      // link must not leave a key that makes file downloads frame-decrypt raw
      // bytes.
      const effectiveKey = outcome.data.Package.IsProtected ? key : undefined;

      const decodedItems = await decodeItems(outcome.data.Items, effectiveKey, id);
      if (current !== attempt) {
        return;
      }

      setPackageKey(effectiveKey);
      setAccessSessionToken(outcome.data.AccessSession.Token);
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

  const sessionExpired = (failedToken?: string) => {
    if (failedToken !== undefined && failedToken !== accessSessionToken()) {
      // A stale request from an older session must not clear a newer grant.
      return;
    }

    clearAccessSession(packageId());
    setAccessSessionToken(undefined);
    setDetails(null);
    setItems([]);
    setPackageKey(undefined);
    setStatus('locked');
    setGateMessage({
      title: t('gate.sessionExpiredTitle'),
      body: t('gate.sessionExpiredBody')
    });
  };

  const packageGone = () => {
    clearAccessSession(packageId());
    setAccessSessionToken(undefined);
    setDetails(null);
    setItems([]);
    setPackageKey(undefined);
    setStatus('gone');
    setGateMessage(null);
  };

  const start = ({ id, secret }: { id: string; secret: string | undefined }) => {
    // Cancel any request from the previous package id before starting over.
    attempt += 1;
    setDetails(null);
    setItems([]);
    setPackageKey(undefined);
    setAccessSessionToken(undefined);
    setGateMessage(null);
    setPassphrase('');

    if (secret !== undefined) {
      setPassphrase(secret);
      void unlockWith(id, secret);
      return;
    }

    const fragment = fragmentPassphrase();
    if (fragment !== undefined) {
      setPassphrase(fragment);
      void unlockWith(id, fragment);
      return;
    }

    setStatus('locked');
  };

  // Memos so the effect restarts only when the id or secret value changes, not
  // on every router location update.
  const currentId = createMemo(packageId);
  const currentSecret = createMemo(initialSecret);
  createEffect(() => ({ id: currentId(), secret: currentSecret() }), start);

  const unlock = () => {
    void unlockWith(packageId(), passphrase());
  };

  return {
    packageId,
    status,
    details,
    items,
    packageKey,
    accessSessionToken,
    passphrase,
    setPassphrase,
    gateMessage,
    sessionExpired,
    packageGone,
    unlock
  };
}
