import { useI18n } from '../../../i18n/i18n.tsx';
import { operator } from '../../../shared/legal/operator.ts';

export function ImprintPage() {
  const { t } = useI18n();

  return (
    <section class="mx-auto grid w-full max-w-[62ch] gap-6">
      <div class="grid gap-1.5">
        <h1 class="text-2xl font-semibold tracking-tight text-ink">{t('imprint.title')}</h1>
        <p class="text-sm text-muted">{t('imprint.intro')}</p>
      </div>
      <dl class="grid gap-3 text-sm">
        <div class="grid gap-0.5">
          <dt class="tiny-label">{t('imprint.providerLabel')}</dt>
          <dd>
            <address class="grid text-ink not-italic">
              <span>{operator.name}</span>
              <span>{operator.street}</span>
              <span>{operator.city}</span>
              <span>{operator.country}</span>
            </address>
          </dd>
        </div>
        <div class="grid gap-0.5">
          <dt class="tiny-label">{t('imprint.contactLabel')}</dt>
          <dd class="grid justify-items-start gap-0.5">
            <a class="text-brand transition hover:text-brand-hover" href={`mailto:${operator.email}`}>
              {operator.email}
            </a>
            <a
              class="text-brand transition hover:text-brand-hover"
              href={operator.github}
              rel="noreferrer"
              target="_blank"
            >
              {t('imprint.contactLink')}
            </a>
          </dd>
        </div>
      </dl>
    </section>
  );
}
