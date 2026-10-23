import { AuthProvider } from '../features/auth/auth-context.tsx';
import { I18nProvider } from '../i18n/i18n.tsx';
import { ThemeProvider } from '../shared/theme/theme-context.tsx';
import { Router } from './router.tsx';

export function AppProviders() {
  return (
    <ThemeProvider>
      <I18nProvider>
        <AuthProvider>
          <Router />
        </AuthProvider>
      </I18nProvider>
    </ThemeProvider>
  );
}
