import IconInbox from '~icons/lucide/inbox';
import { useI18n } from '../../../i18n/i18n.tsx';

export function DashboardStage() {
  const { t } = useI18n();

  return (
    <div class="grid justify-items-center gap-2 rounded-xl border border-dashed border-border-soft py-10 text-center">
      <IconInbox class="h-6 w-6 text-muted" />
      <div class="font-mono text-sm text-muted">{t('account.dashboard.empty')}</div>
      <div class="font-sans text-xs text-muted/80">{t('account.notWired')}</div>
    </div>
  );
}
