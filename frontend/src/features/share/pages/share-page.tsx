import { useLocation, useParams } from '@solidjs/router';
import { Show } from 'solid-js';
import { useI18n } from '../../../i18n/i18n.tsx';
import { isValidId } from '../../../shared/crypto/ids.ts';
import { Gate } from '../components/gate.tsx';
import { ShareSkeleton } from '../components/share-skeleton.tsx';
import { SharedDocument } from '../components/shared-document.tsx';
import { createShare } from '../use-share.ts';

function InvalidLink() {
  const { t } = useI18n();

  return (
    <div class="grid place-items-center px-2 py-10 sm:py-16">
      <div class="gate">
        <h1 class="text-xl font-semibold tracking-tight text-ink">{t('share.invalidLinkTitle')}</h1>
        <p class="text-sm text-muted">{t('share.invalidLinkBody')}</p>
        <a class="btn btn-secondary w-fit" href="/">
          {t('gate.home')}
        </a>
      </div>
    </div>
  );
}

function ShareView(props: { packageId: string; secret: string | undefined }) {
  const share = createShare(
    () => props.packageId,
    () => props.secret
  );

  const gateVisible = () => {
    const status = share.status();
    return status === 'locked' || status === 'invalid' || status === 'gone';
  };

  return (
    <Show
      when={share.status() === 'unlocked' ? share.details() : null}
      fallback={
        <Show when={gateVisible()} fallback={<ShareSkeleton />}>
          <Gate
            gone={share.status() === 'gone'}
            passphrase={share.passphrase()}
            message={share.gateMessage()}
            onPassphraseInput={share.setPassphrase}
            onSubmit={share.unlock}
          />
        </Show>
      }
    >
      {(details) => (
        <SharedDocument
          package={details().Package}
          items={share.items()}
          packageId={share.packageId()}
          packageKey={share.packageKey()}
          accessSessionToken={share.accessSessionToken()}
          accessSessionExpiresAt={details().AccessSession.ExpiresAt}
          onAccessExpired={share.sessionExpired}
          onPackageGone={share.packageGone}
        />
      )}
    </Show>
  );
}

/**
 * The share route is UUID-only. Codes are resolved by the code-entry form and
 * never become part of the URL. The code's client secret arrives in router
 * state so this page can unlock immediately without a password gate.
 */
export function SharePage() {
  const params = useParams();
  const location = useLocation<{ secret?: string }>();
  const packageId = () => params.packageId ?? '';

  return (
    <Show when={isValidId(packageId())} fallback={<InvalidLink />}>
      <ShareView packageId={packageId()} secret={location.state?.secret} />
    </Show>
  );
}
