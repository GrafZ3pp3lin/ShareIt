import { Show } from 'solid-js';
import { DashboardStage } from '../components/dashboard-stage.tsx';
import { LoginStage } from '../components/login-stage.tsx';
import { useAuth } from '../../auth/auth-context.tsx';
import { TerminalWindow } from '../../../shared/components/terminal-window.tsx';
import { useI18n } from '../../../i18n/i18n.tsx';

export function AccountPage() {
  const auth = useAuth();
  const { t } = useI18n();

  return (
    <div class="mx-auto w-full max-w-2xl">
      <TerminalWindow label={t('account.windowLabel')}>
        <Show
          when={auth.isAuthenticated}
          fallback={
            <LoginStage
              enabled={auth.isEnabled}
              heading={t('account.login.heading')}
              body={t('account.login.body')}
              ctaLabel={t('account.login.cta')}
              continueLabel={t('account.login.continueWithoutAccount')}
              disabledNotice={t('auth.oidcNotConfiguredLong')}
              onLogin={auth.login}
            />
          }
        >
          <DashboardStage emptyLabel={t('account.dashboard.empty')} notWiredLabel={t('account.notWired')} />
        </Show>
      </TerminalWindow>
    </div>
  );
}
