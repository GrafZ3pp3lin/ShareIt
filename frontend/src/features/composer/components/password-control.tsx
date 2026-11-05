import { clsx } from 'clsx/lite';
import { For, Show, createSignal } from 'solid-js';
import IconRotateCw from '~icons/lucide/rotate-cw';
import { useI18n } from '../../../i18n/i18n.tsx';
import { CopyButton } from '../../../shared/components/copy-button.tsx';
import type { PassphraseStrength } from '../../../shared/crypto/passphrase.ts';
import type { PasswordMode } from '../use-composer.ts';

type PasswordControlProps = {
  mode: PasswordMode;
  customPassphrase: string;
  customError: string | null;
  strength: PassphraseStrength;
  generatedPassphrase: string;
  onModeChange: (mode: PasswordMode) => void;
  onCustomInput: (value: string) => void;
  onRegenerate: () => void;
};

const MODES: PasswordMode[] = ['off', 'generate', 'custom'];

const modeLabelKey = (mode: PasswordMode) => {
  if (mode === 'generate') {
    return 'options.password.mode.generate';
  }
  return mode === 'custom' ? 'options.password.mode.custom' : 'options.password.mode.off';
};

export function PasswordControl(props: PasswordControlProps) {
  const { t } = useI18n();
  const [revealed, setRevealed] = createSignal(false);

  const strengthLabel = () => {
    const strength = props.strength;
    if (strength <= 1) {
      return t('options.password.strength.weak');
    }
    return strength === 2 ? t('options.password.strength.ok') : t('options.password.strength.strong');
  };

  return (
    <div class="grid gap-2">
      <div class="option-row">
        <span class="grid gap-0.5">
          <span>{t('options.password.label')}</span>
          <span class="text-[10px] text-muted/85">{t('options.password.hint')}</span>
        </span>
        <div class="segmented" role="group" aria-label={t('options.password.label')}>
          <For each={MODES}>
            {(mode) => (
              <button
                type="button"
                class={clsx(
                  'cursor-pointer border-l border-border-soft px-2.5 py-1 font-mono text-[11px] text-muted transition first:border-l-0',
                  props.mode === mode && 'bg-ink text-panel'
                )}
                aria-pressed={props.mode === mode ? 'true' : 'false'}
                onClick={() => props.onModeChange(mode)}
              >
                {t(modeLabelKey(mode))}
              </button>
            )}
          </For>
        </div>
      </div>

      <Show when={props.mode === 'custom'}>
        <div class="grid gap-2 rounded-lg border border-dashed border-border-soft bg-panel-soft p-2.5">
          <div class="flex items-center gap-2 rounded-md border border-border-soft bg-panel px-2.5 py-2">
            <input
              type={revealed() ? 'text' : 'password'}
              class="min-w-0 flex-1 bg-transparent font-mono text-xs text-ink outline-none"
              value={props.customPassphrase}
              aria-label={t('options.password.custom')}
              autocomplete="new-password"
              onInput={(event) => props.onCustomInput(event.currentTarget.value)}
            />
            <button
              type="button"
              class="cursor-pointer font-mono text-[10.5px] text-muted transition hover:text-ink"
              onClick={() => setRevealed((value) => !value)}
            >
              {revealed() ? t('options.password.hide') : t('options.password.show')}
            </button>
          </div>

          <div class="flex flex-wrap items-center justify-between gap-2">
            <div class="flex items-center gap-1 font-mono text-[10.5px] text-muted">
              <For each={[1, 2, 3]}>
                {(level) => (
                  <i
                    class={clsx(
                      'h-1 w-4 rounded-sm',
                      props.customError === null && props.strength >= level ? 'bg-ink' : 'bg-border-soft'
                    )}
                  />
                )}
              </For>
              <span class="ml-1">
                {props.customError === null && props.customPassphrase.length > 0 ? strengthLabel() : ''}
              </span>
            </div>
            <span class="font-mono text-[10.5px] text-muted">{t('options.password.suggestion')}</span>
          </div>

          <Show when={props.customError}>
            <p class="font-mono text-[10.5px] text-ink">{props.customError}</p>
          </Show>
          <p class="font-mono text-[10.5px] leading-relaxed text-muted">{t('options.password.note')}</p>
        </div>
      </Show>

      <Show when={props.mode === 'generate'}>
        <div class="grid gap-2 rounded-lg border border-dashed border-border-soft bg-panel-soft p-2.5">
          <div class="flex items-center gap-2 rounded-md border border-border-soft bg-panel px-2.5 py-2">
            <span class="min-w-0 flex-1 truncate font-mono text-xs text-ink">{props.generatedPassphrase}</span>
            <CopyButton value={props.generatedPassphrase} variant="inline" />
          </div>
          <div class="flex flex-wrap items-center justify-between gap-2 font-mono text-[10.5px] text-muted">
            <span>{t('options.password.shownOnce')}</span>
            <button
              type="button"
              class="inline-flex cursor-pointer items-center gap-1 text-muted transition hover:text-ink"
              onClick={props.onRegenerate}
            >
              <IconRotateCw class="h-3 w-3" />
              {t('options.password.regenerate')}
            </button>
          </div>
        </div>
      </Show>
    </div>
  );
}
