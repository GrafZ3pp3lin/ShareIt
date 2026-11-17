import { Show } from 'solid-js';
import { useI18n } from '../../../i18n/i18n.tsx';
import { CommitBar } from '../components/commit-bar.tsx';
import { Composer } from '../components/composer.tsx';
import { OptionsPanel } from '../components/options-panel.tsx';
import { ResultPanel } from '../components/result-panel.tsx';
import { createComposer } from '../use-composer.ts';

export function ComposerPage() {
  const { t } = useI18n();
  const composer = createComposer();

  return (
    <Show
      when={composer.stage() === 'form'}
      fallback={
        <Show when={composer.result()}>
          {(result) => <ResultPanel result={result()} onReset={composer.resetForm} />}
        </Show>
      }
    >
      <div class="mx-auto grid w-full max-w-190 gap-4">
        <div class="grid gap-1.5">
          <h1 class="text-2xl font-semibold tracking-tight text-ink">{t('composer.headline')}</h1>
          <p class="max-w-[62ch] text-sm text-muted">{t('composer.subline')}</p>
        </div>

        <form class="grid gap-3" onSubmit={composer.handleSubmit}>
          <Composer
            text={composer.text()}
            files={composer.files()}
            totalSize={composer.totalSize()}
            limitLabel={composer.limitLabel()}
            onTextInput={composer.setText}
            onAddFiles={composer.addFiles}
            onRemoveFile={composer.removeFile}
            onSubmitShortcut={() => composer.handleSubmit()}
          />

          <Show when={!composer.isAuthenticated()}>
            <p class="px-1 font-mono text-[10.5px] leading-relaxed text-muted">{t('composer.guestListNote')}</p>
          </Show>

          <Show when={composer.optionsOpen()}>
            <OptionsPanel
              isAuthenticated={composer.isAuthenticated()}
              expiryIndex={composer.expiryIndex()}
              accessIndex={composer.accessIndex()}
              passwordMode={composer.passwordMode()}
              customPassphrase={composer.customPassphrase()}
              customError={composer.passphraseError()}
              strength={composer.passphraseCheck().strength}
              generatedPassphrase={composer.generatedPassphrase()}
              onExpiryChange={composer.setExpiry}
              onDownloadsChange={composer.setDownloads}
              onPasswordModeChange={composer.setPasswordMode}
              onCustomPassphraseInput={composer.setCustomPassphrase}
              onRegeneratePassphrase={composer.regeneratePassphrase}
            />
          </Show>

          <CommitBar
            summary={composer.summary()}
            optionsOpen={composer.optionsOpen()}
            canSubmit={composer.canSubmit()}
            submitting={composer.submitting()}
            onToggleOptions={() => composer.setOptionsOpen((open) => !open)}
            onCancel={composer.handleCancel}
            showCancel={composer.progress() !== null}
          />

          <Show when={composer.progress()}>
            <div
              class="flex items-center justify-between gap-3 font-mono text-xs text-muted"
              role="status"
              aria-live="polite"
            >
              <span>{composer.progressLabel()}</span>
            </div>
          </Show>

          <Show when={composer.error()}>
            {(message) => (
              <div class="grid gap-2">
                <div class="caution" role="alert">
                  {message()}
                </div>
                <Show when={composer.partial()}>
                  <div class="flex flex-wrap gap-2">
                    <button type="button" class="btn btn-secondary" onClick={() => composer.handleSubmit()}>
                      {t('composer.retry')}
                    </button>
                    <button type="button" class="btn btn-ghost" onClick={composer.showLinkAnyway}>
                      {t('composer.showLinkAnyway')}
                    </button>
                  </div>
                </Show>
              </div>
            )}
          </Show>

          <div class="grid gap-2 pt-3">
            <div class="tiny-label">{t('composer.how.title')}</div>
            <div class="grid gap-2 sm:grid-cols-3">
              <p class="text-xs text-muted">{t('composer.how.one')}</p>
              <p class="text-xs text-muted">{t('composer.how.two')}</p>
              <p class="text-xs text-muted">{t('composer.how.three')}</p>
            </div>
            <p class="text-xs text-muted/90">{t('composer.how.note')}</p>
          </div>
        </form>
      </div>
    </Show>
  );
}
