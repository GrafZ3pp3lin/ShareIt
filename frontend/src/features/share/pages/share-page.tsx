import { useParams } from '@solidjs/router';
import { Show } from 'solid-js';
import { Gate } from '../components/gate.tsx';
import { ShareSkeleton } from '../components/share-skeleton.tsx';
import { SharedDocument } from '../components/shared-document.tsx';
import { createShare } from '../use-share.ts';

export function SharePage() {
  const params = useParams();
  const share = createShare(() => params.packageId ?? '');

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
