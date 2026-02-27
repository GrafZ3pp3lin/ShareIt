import { Show } from 'solid-js';
import { Button } from '../../../shared/components/button';
import { NoticeBox } from '../../../shared/components/notice-box';
import { PillBadge } from '../../../shared/components/pill-badge';
import type { UploadResult } from './upload-types';
import { LinkButton } from '../../../shared/components/link-button';

type UploadResultPanelProps = {
  result: UploadResult;
  shareLinkLabel: string;
  copyLinkLabel: string;
  passphraseLabel: string;
  copyPassphraseLabel: string;
  onCopyLink: (link: string) => void;
  onCopyPassphrase: (passphrase: string) => void;
};

export function UploadResultPanel(props: UploadResultPanelProps) {
  return (
    <div class="grid gap-3">
      <NoticeBox>
        <div class="text-ink">{props.shareLinkLabel}</div>
        <div class="mt-2 flex flex-wrap gap-2">
          <LinkButton href={props.result.link} variant='ghost' target="_blank" rel="noreferrer">
            {props.result.link}
          </LinkButton>
          <Button variant="secondary" type="button" onClick={() => props.onCopyLink(props.result.link)}>
            {props.copyLinkLabel}
          </Button>
        </div>
      </NoticeBox>
      <Show when={props.result.passphrase}>
        {(passphrase) => (
          <NoticeBox>
            <div class="text-ink">{props.passphraseLabel}</div>
            <div class="mt-2 flex flex-wrap items-center gap-2">
              <PillBadge as="span">{passphrase()}</PillBadge>
              <Button variant="secondary" type="button" onClick={() => props.onCopyPassphrase(passphrase())}>
                {props.copyPassphraseLabel}
              </Button>
            </div>
          </NoticeBox>
        )}
      </Show>
    </div>
  );
}
