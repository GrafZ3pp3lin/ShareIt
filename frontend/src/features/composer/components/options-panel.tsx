import { For, Show } from 'solid-js';
import { useI18n } from '../../../i18n/i18n.tsx';
import type { PassphraseStrength } from '../../../shared/crypto/passphrase.ts';
import { ACCESS_COUNT_OPTIONS, EXPIRY_OPTIONS } from '../composer-options.ts';
import type { PasswordMode } from '../use-composer.ts';
import { PasswordControl } from './password-control.tsx';

type OptionsPanelProps = {
  isAuthenticated: boolean;
  expiryIndex: number;
  accessIndex: number;
  passwordMode: PasswordMode;
  customPassphrase: string;
  customError: string | null;
  strength: PassphraseStrength;
  generatedPassphrase: string;
  onExpiryChange: (index: number) => void;
  onDownloadsChange: (index: number) => void;
  onPasswordModeChange: (mode: PasswordMode) => void;
  onCustomPassphraseInput: (value: string) => void;
  onRegeneratePassphrase: () => void;
};

/**
 * Expanded link options: expiry, accesses and password. Guests only get the
 * password row; the fixed expiry/access values stay in the collapsed summary.
 * Code mode hides the pickers and shows its fixed guardrails instead.
 */
export function OptionsPanel(props: OptionsPanelProps) {
  const { t } = useI18n();

  const downloadLabel = (count: number | null) => {
    if (count == null) {
      return t('options.downloads.unlimited');
    }
    return count === 1 ? t('options.downloads.once') : t('options.downloads.count', { count });
  };

  return (
    <div id="composer-options" class="grid gap-2 rounded-xl border border-border-soft bg-panel p-3.5">
      <Show
        when={props.passwordMode !== 'code'}
        fallback={
          <div class="rounded-lg border border-dashed border-border-soft bg-panel-soft p-2.5">
            <p class="font-mono text-[10.5px] leading-relaxed text-muted">{t('options.code.guardrails')}</p>
          </div>
        }
      >
        <Show when={props.isAuthenticated}>
          <label class="option-row">
            <span>{t('options.expiry.label')}</span>
            <select
              class="cursor-pointer border-0 bg-transparent font-mono text-xs font-medium text-ink outline-none"
              value={props.expiryIndex}
              onChange={(event) => props.onExpiryChange(Number(event.currentTarget.value))}
            >
              <For each={EXPIRY_OPTIONS}>
                {(option, index) => <option value={index()}>{t(option.labelKey)}</option>}
              </For>
            </select>
          </label>

          <label class="option-row">
            <span class="grid gap-0.5">
              <span>{t('options.downloads.label')}</span>
              <span class="text-[10px] text-muted/85">{t('options.downloads.hint')}</span>
            </span>
            <select
              class="cursor-pointer border-0 bg-transparent font-mono text-xs font-medium text-ink outline-none"
              value={props.accessIndex}
              onChange={(event) => props.onDownloadsChange(Number(event.currentTarget.value))}
            >
              <For each={ACCESS_COUNT_OPTIONS}>
                {(count, index) => <option value={index()}>{downloadLabel(count)}</option>}
              </For>
            </select>
          </label>
        </Show>
      </Show>

      <PasswordControl
        mode={props.passwordMode}
        customPassphrase={props.customPassphrase}
        customError={props.customError}
        strength={props.strength}
        generatedPassphrase={props.generatedPassphrase}
        onModeChange={props.onPasswordModeChange}
        onCustomInput={props.onCustomPassphraseInput}
        onRegenerate={props.onRegeneratePassphrase}
      />

      <Show when={!props.isAuthenticated && props.passwordMode !== 'code'}>
        <p class="px-1 font-mono text-[10.5px] leading-relaxed text-muted">{t('options.guestNote')}</p>
      </Show>
    </div>
  );
}
