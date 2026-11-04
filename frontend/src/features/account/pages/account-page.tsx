import { Show } from 'solid-js';
import { DashboardStage } from '../components/dashboard-stage.tsx';
import { LoginStage } from '../components/login-stage.tsx';
import { useAuth } from '../../auth/auth-context.tsx';

export function AccountPage() {
  const auth = useAuth();

  return (
    <div class="surface-card mx-auto w-full max-w-2xl">
      <Show
        when={auth.isAuthenticated()}
        fallback={<LoginStage enabled={auth.isEnabled} onLogin={() => void auth.login()} />}
      >
        <DashboardStage />
      </Show>
    </div>
  );
}
