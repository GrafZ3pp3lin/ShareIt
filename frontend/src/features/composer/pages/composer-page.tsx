import { Show } from 'solid-js';
import { useI18n } from '../../../i18n/i18n.tsx';
import { CommitBar } from '../components/commit-bar.tsx';
import { CodeEntry } from '../components/code-entry.tsx';
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
      <div class="grid w-full gap-4">
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
            {(current) => (
              <div class="grid gap-1 font-mono text-xs text-muted">
                <span>{composer.progressLabel()}</span>
                {/* A native, labeled progress bar. The accessible label is
                    static; every percentage tick is deliberately not
                    announced through aria-live. */}
                <progress
                  class="h-1.5 w-full appearance-none rounded-full [&::-webkit-progress-bar]:rounded-full [&::-webkit-progress-bar]:bg-panel-soft [&::-webkit-progress-value]:rounded-full [&::-webkit-progress-value]:bg-brand"
                  aria-label={t('composer.progress.label')}
                  max={current().phase === 'uploading' ? current().totalBytes : undefined}
                  value={current().phase === 'uploading' ? current().sentBytes : undefined}
                />
              </div>
            )}
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
                  </div>
                </Show>
              </div>
            )}
          </Show>

          <div class="grid gap-3 border-t border-border-soft pt-4">
            <div class="tiny-label">{t('composer.how.title')}</div>
            <ol class="grid list-none gap-3 p-0 sm:grid-cols-3">
              <li class="flex gap-2.5">
                <span class="mt-px font-mono text-[10px] font-semibold text-brand">01</span>
                <span class="text-xs leading-relaxed text-muted">{t('composer.how.one')}</span>
              </li>
              <li class="flex gap-2.5">
                <span class="mt-px font-mono text-[10px] font-semibold text-brand">02</span>
                <span class="text-xs leading-relaxed text-muted">{t('composer.how.two')}</span>
              </li>
              <li class="flex gap-2.5">
                <span class="mt-px font-mono text-[10px] font-semibold text-brand">03</span>
                <span class="text-xs leading-relaxed text-muted">{t('composer.how.three')}</span>
              </li>
            </ol>
          </div>
        </form>

        <div class="border-t border-border-soft pt-4">
          <div class="max-w-sm">
            <CodeEntry />
          </div>
        </div>
      </div>
    </Show>
  );
}
