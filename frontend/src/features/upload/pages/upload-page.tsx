import { Show } from 'solid-js';
import { UploadFilePanel, UploadMetadataFields, UploadTextPanel } from '../components/upload-form-fields.tsx';
import { UploadResultPanel } from '../components/upload-result-panel.tsx';
import { createUploader } from '../use-uploader.ts';
import { TerminalWindow } from '../../../shared/components/terminal-window.tsx';
import { useI18n } from '../../../i18n/i18n.tsx';

export function UploadPage() {
  const { t } = useI18n();
  const uploader = createUploader();

  return (
    <div class="mx-auto w-full max-w-2xl">
      <TerminalWindow label={t('upload.windowLabel')}>
        <Show when={uploader.stage() === 'form'}>
          <form class="grid gap-5" onSubmit={uploader.handleSubmit}>
            <details>
              <summary class="cursor-pointer font-mono text-xs text-muted transition hover:text-ink">
                {t('upload.details.toggle')}
              </summary>
              <div class="mt-3 grid gap-3">
                <UploadMetadataFields
                  title={uploader.title()}
                  description={uploader.description()}
                  onTitleInput={uploader.setTitle}
                  onDescriptionInput={uploader.setDescription}
                />
              </div>
            </details>

            <div class="grid gap-4 sm:grid-cols-2 min-h-42">
              <UploadFilePanel files={uploader.files()} onFilesChange={uploader.handleFilesChange} />
              <UploadTextPanel text={uploader.text()} onTextInput={uploader.setText} />
            </div>

            <div class="grid gap-2">
              <div class="flex flex-wrap items-center gap-3">
                <button type="button" class="pill-badge cursor-pointer" onClick={uploader.cycleExpiry}>
                  <span>{t('upload.expiry.label')}:</span>
                  <span class="text-ink">{uploader.expiry().label}</span>
                </button>
                <button type="button" class="pill-badge cursor-pointer" onClick={uploader.toggleEncrypt}>
                  <span>{t('upload.password.label')}:</span>
                  <span class="text-ink">
                    {uploader.encrypt() ? t('upload.password.on') : t('upload.password.off')}
                  </span>
                </button>
                <Show when={uploader.limitLabel()}>{(limit) => <span class="text-xs text-muted">{limit()}</span>}</Show>
              </div>
              <p class="text-xs text-muted">{t('upload.password.caption')}</p>
            </div>

            <button class="btn btn-primary" type="submit" disabled={uploader.submitting()}>
              {uploader.submitting()
                ? t('upload.submit.uploading')
                : uploader.partial()
                  ? t('upload.recovery.retry')
                  : t('upload.submitCommand')}
            </button>

            <Show when={uploader.progress()}>
              <div class="flex items-center justify-between gap-3 font-mono text-xs text-muted">
                <span>{uploader.progressLabel()}</span>
                <button
                  class="cursor-pointer underline transition hover:text-ink"
                  type="button"
                  onClick={uploader.handleCancel}
                >
                  {t('upload.submit.cancel')}
                </button>
              </div>
            </Show>

            <Show when={uploader.error()}>
              <div class="grid gap-2">
                <div class="notice-box">{uploader.error()}</div>
                <Show when={uploader.partial()}>
                  <button class="btn btn-secondary" type="button" onClick={uploader.showLinkAnyway}>
                    {t('upload.recovery.showLink')}
                  </button>
                </Show>
              </div>
            </Show>
          </form>
        </Show>

        <Show when={uploader.stage() === 'result' ? uploader.result() : null}>
          {(value) => <UploadResultPanel result={value()} onReset={uploader.resetForm} />}
        </Show>
      </TerminalWindow>
    </div>
  );
}
