import { useI18n } from '../../../i18n/i18n.tsx';

export function NotFoundPage() {
  const { t } = useI18n();

  return (
    <div class="grid place-items-center px-4 py-20 text-center">
      <div class="grid max-w-md gap-3">
        <h1 class="text-xl font-semibold text-ink">{t('app.notFound.title')}</h1>
        <p class="text-sm text-muted">{t('app.notFound.body')}</p>
        <a class="text-sm font-medium text-brand transition hover:text-brand-hover" href="/">
          {t('app.notFound.home')}
        </a>
      </div>
    </div>
  );
}
