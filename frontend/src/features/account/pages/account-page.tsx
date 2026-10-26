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
          when={auth.isAuthenticated()}
          fallback={<LoginStage enabled={auth.isEnabled} onLogin={() => void auth.login()} />}
        >
          <DashboardStage />
        </Show>
      </TerminalWindow>
    </div>
  );
}
