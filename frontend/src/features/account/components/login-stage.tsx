import { Show } from 'solid-js';
import IconLock from '~icons/lucide/lock';

type LoginStageProps = {
  enabled: boolean;
  heading: string;
  body: string;
  ctaLabel: string;
  continueLabel: string;
  disabledNotice: string;
  onLogin: () => void;
};

export function LoginStage(props: LoginStageProps) {
  return (
    <div class="mx-auto grid max-w-sm gap-4 py-4 text-center">
      <div class="mx-auto flex h-10 w-10 items-center justify-center rounded-full bg-panel-soft">
        <IconLock class="h-4 w-4 text-muted" />
      </div>
      <div class="grid gap-1">
        <h2 class="font-mono text-sm font-semibold text-ink">{props.heading}</h2>
        <p class="font-sans text-xs text-muted">{props.body}</p>
      </div>
      <Show when={props.enabled} fallback={<div class="notice-box font-sans">{props.disabledNotice}</div>}>
        <button type="button" class="btn btn-primary" onClick={props.onLogin}>
          {props.ctaLabel}
        </button>
      </Show>
      <a class="font-sans text-xs text-muted transition hover:text-ink" href="/">
        {props.continueLabel}
      </a>
    </div>
  );
}
