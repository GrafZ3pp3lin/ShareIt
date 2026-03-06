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
        <div class="mt-2 flex flex-col gap-2 sm:flex-row sm:flex-wrap">
          <a
            class="btn btn-ghost w-full justify-start overflow-hidden text-wrap break-all sm:w-auto"
            href={props.result.link}
            target="_blank"
            rel="noreferrer"
          >
            {props.result.link}
          </a>
          <button class="btn btn-secondary w-full sm:w-auto" type="button" onClick={() => props.onCopyLink(props.result.link)}>
            {props.copyLinkLabel}
          </button>
        </div>
      </div>
      <Show when={props.result.passphrase}>
        {(passphrase) => (
          <div class="notice-box">
            <div class="text-ink">{props.passphraseLabel}</div>
            <div class="mt-2 flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center">
              <span class="pill-badge w-full break-all sm:w-auto">{passphrase()}</span>
              <button class="btn btn-secondary w-full sm:w-auto" type="button" onClick={() => props.onCopyPassphrase(passphrase())}>
                {props.copyPassphraseLabel}
              </button>
            </div>
          </div>
        )}
      </Show>
    </div>
  );
}
