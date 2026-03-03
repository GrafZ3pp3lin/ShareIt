import { Show } from 'solid-js';
import type { UploadResult } from './upload-types';

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
      <div class="notice-box">
        <div class="text-ink">{props.shareLinkLabel}</div>
        <div class="mt-2 flex flex-wrap gap-2">
          <a class="btn btn-ghost" href={props.result.link} target="_blank" rel="noreferrer">
            {props.result.link}
          </a>
          <button class="btn btn-secondary" type="button" onClick={() => props.onCopyLink(props.result.link)}>
            {props.copyLinkLabel}
          </button>
        </div>
      </div>
      <Show when={props.result.passphrase}>
        {(passphrase) => (
          <div class="notice-box">
            <div class="text-ink">{props.passphraseLabel}</div>
            <div class="mt-2 flex flex-wrap items-center gap-2">
              <span class="pill-badge">{passphrase()}</span>
              <button class="btn btn-secondary" type="button" onClick={() => props.onCopyPassphrase(passphrase())}>
                {props.copyPassphraseLabel}
              </button>
            </div>
          </div>
        )}
      </Show>
    </div>
  );
}
