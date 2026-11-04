import { clsx } from 'clsx/lite';
import { createSignal, onCleanup } from 'solid-js';
import { useI18n } from '../../i18n/i18n.tsx';

type CopyButtonProps = {
  value: string;
  label?: string;
  copiedLabel?: string;
  class?: string;
  variant?: 'button' | 'inline';
};

export function CopyButton(props: CopyButtonProps) {
  const { t } = useI18n();
  const [copied, setCopied] = createSignal(false);
  let resetTimer: ReturnType<typeof setTimeout> | undefined;

  onCleanup(() => {
    if (resetTimer) {
      clearTimeout(resetTimer);
    }
  });

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(props.value);
    } catch {
      return;
    }
    setCopied(true);
    if (resetTimer) {
      clearTimeout(resetTimer);
    }
    resetTimer = setTimeout(() => setCopied(false), 1500);
  };

  return (
    <button
      type="button"
      class={clsx(
        props.variant === 'inline'
          ? 'cursor-pointer font-mono text-[11px] font-semibold text-ink transition hover:text-brand'
          : 'btn btn-secondary',
        props.class
      )}
      onClick={() => void copy()}
    >
      <span aria-live="polite">
        {copied() ? (props.copiedLabel ?? t('app.copied')) : (props.label ?? t('app.copy'))}
      </span>
    </button>
  );
}
