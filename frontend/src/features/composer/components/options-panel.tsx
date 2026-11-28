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
 * Expanded link options as one flat list of separated setting rows: expiry,
 * accesses and password. Guests only get the password row; code mode hides the
 * expiry/access rows. The password mode picker and its details stay in the
 * password row's block.
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
    <div id="composer-options" class="divide-y divide-border-soft rounded-xl border border-border-soft bg-panel px-4">
      <Show when={props.isAuthenticated && props.passwordMode !== 'code'}>
        <label class="setting-row py-3">
          <span>{t('options.expiry.label')}</span>
          <select
            class="setting-select"
            value={props.expiryIndex}
            onChange={(event) => props.onExpiryChange(Number(event.currentTarget.value))}
          >
            <For each={EXPIRY_OPTIONS}>{(option, index) => <option value={index()}>{t(option.labelKey)}</option>}</For>
          </select>
        </label>

        <label class="setting-row py-3">
          <span>{t('options.downloads.label')}</span>
          <select
            class="setting-select"
            value={props.accessIndex}
            onChange={(event) => props.onDownloadsChange(Number(event.currentTarget.value))}
          >
            <For each={ACCESS_COUNT_OPTIONS}>
              {(count, index) => <option value={index()}>{downloadLabel(count)}</option>}
            </For>
          </select>
        </label>
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
        <p class="note py-3">{t('options.guestNote')}</p>
      </Show>
    </div>
  );
}
