import IconLink from '~icons/lucide/link';
import { useI18n } from '../../../i18n/i18n.tsx';

export function EmptyState() {
  const { t } = useI18n();

  return (
    <div class="grid place-items-center px-2 py-12 sm:py-16">
      <div class="grid w-full max-w-105 justify-items-center gap-3 rounded-2xl border border-dashed border-border-soft px-6 py-8 text-center">
        <div
          class="grid h-10 w-10 place-items-center rounded-full border border-border-soft bg-panel-soft text-muted"
          aria-hidden="true"
        >
          <IconLink class="h-4 w-4" />
        </div>
        <h1 class="text-xl font-semibold tracking-tight text-ink">{t('links.empty.title')}</h1>
        <p class="max-w-[46ch] text-sm text-muted">{t('links.empty.body')}</p>
        <a class="btn btn-primary" href="/">
          {t('links.empty.cta')}
        </a>
      </div>
    </div>
  );
}
