import { createSignal, Show } from 'solid-js';
import { useNavigate } from '@solidjs/router';
import { useI18n } from '../../../i18n/i18n.tsx';
import { resolveShortCode } from '../../../shared/api/content.ts';
import { normalizeShortCode, splitShortCode } from '../../../shared/short-code.ts';

/**
 * Entry point for recipients who only have the six-character code. Only the
 * three-character locator is sent to the server; the secret travels to the
 * share page in router state, never in the URL.
 */
export function CodeEntry() {
  const { t } = useI18n();
  const navigate = useNavigate();
  const [code, setCode] = createSignal('');
  const [error, setError] = createSignal<string | null>(null);
  const [submitting, setSubmitting] = createSignal(false);

  const rateLimitMessage = (seconds: number | undefined) => {
    if (seconds == null || seconds <= 0) {
      return t('composer.codeRateLimitedUnknown');
    }

    const total = Math.max(1, Math.ceil(seconds));
    if (total < 60) {
      return t('composer.codeRateLimited', {
        wait: t('composer.codeRetryWaitSeconds', { count: total })
      });
    }

    return t('composer.codeRateLimited', {
      wait: t('composer.codeRetryWaitMinutes', { count: Math.ceil(total / 60) })
    });
  };

  const submit = async (event: Event) => {
    event.preventDefault();
    if (submitting()) {
      return;
    }

    const normalized = normalizeShortCode(code());
    if (!normalized) {
      setError(t('composer.codeInvalid'));
      return;
    }

    setError(null);
    setSubmitting(true);

    const { locator, secret } = splitShortCode(normalized);

    try {
      const outcome = await resolveShortCode(locator);
      if (outcome.data) {
        // The UUID route stays canonical; the client secret is handed to it in
        // router state so the key can be derived without a password gate.
        navigate(`/s/${outcome.data.PackageId}`, { replace: true, state: { secret } });
        return;
      }

      if (outcome.status === 429) {
        setError(rateLimitMessage(outcome.retryAfterSeconds));
      } else if (outcome.status === 404) {
        setError(t('composer.codeNotFound'));
      } else {
        setError(t('composer.codeFailed'));
      }
    } catch {
      setError(t('composer.codeFailed'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <form
      class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between sm:gap-6"
      aria-labelledby="short-code-heading"
      onSubmit={submit}
    >
      <div class="grid gap-1">
        <label id="short-code-heading" class="tiny-label" for="short-code-entry">
          {t('composer.haveCode')}
        </label>
        <p id="short-code-hint" class="text-xs leading-relaxed text-muted">
          {t('composer.haveCodeHint')}
        </p>
      </div>

      <div class="grid w-full gap-1.5 sm:w-80 sm:shrink-0">
        <div class="flex gap-2">
          <input
            id="short-code-entry"
            class="text-field min-w-0 flex-1 font-mono tracking-[0.08em] uppercase"
            type="text"
            inputmode="text"
            autocomplete="off"
            autocapitalize="characters"
            spellcheck={false}
            placeholder={t('composer.codePlaceholder')}
            value={code()}
            disabled={submitting()}
            aria-describedby={error() ? 'short-code-hint short-code-error' : 'short-code-hint'}
            aria-invalid={error() ? 'true' : undefined}
            onInput={(event) => {
              setCode(event.currentTarget.value);
              setError(null);
            }}
          />
          <button class="btn btn-secondary shrink-0" type="submit" disabled={submitting()}>
            {submitting() ? t('composer.codeOpening') : t('composer.codeSubmit')}
          </button>
        </div>
        <Show when={error()}>
          <p id="short-code-error" class="font-mono text-[10.5px] text-muted" role="alert">
            {error()}
          </p>
        </Show>
      </div>
    </form>
  );
}
