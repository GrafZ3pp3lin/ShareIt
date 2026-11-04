import { useI18n } from '../../../i18n/i18n.tsx';

type CommitBarProps = {
  summary: string;
  optionsOpen: boolean;
  canSubmit: boolean;
  submitting: boolean;
  onToggleOptions: () => void;
  onCancel: () => void;
  showCancel: boolean;
};

/**
 * Sticky bottom bar: the collapsed options summary on the left, the primary
 * action on the right. The expanded panel lives above it, never inside.
 */
export function CommitBar(props: CommitBarProps) {
  const { t } = useI18n();

  return (
    <div class="commit-bar">
      <button
        type="button"
        class="flex cursor-pointer flex-wrap items-center gap-1.5 text-left font-mono text-[11.5px] text-muted"
        aria-expanded={props.optionsOpen ? 'true' : 'false'}
        onClick={props.onToggleOptions}
      >
        <span>{props.summary}</span>
        <span class="text-[9px] opacity-70" aria-hidden="true">
          {props.optionsOpen ? '▴' : '▾'}
        </span>
      </button>

      <div class="flex items-center gap-3">
        {props.showCancel ? (
          <button
            type="button"
            class="cursor-pointer font-mono text-xs text-muted underline transition hover:text-ink"
            onClick={props.onCancel}
          >
            {t('composer.cancel')}
          </button>
        ) : null}
        <button type="submit" class="btn btn-primary" disabled={!props.canSubmit}>
          {props.submitting ? t('composer.submitting') : t('composer.submit')}
        </button>
      </div>
    </div>
  );
}
