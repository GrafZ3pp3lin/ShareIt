import { QueryClient, QueryClientProvider } from '@tanstack/solid-query';
import { RouterProvider } from '@tanstack/solid-router';
import { AuthProvider } from '../features/auth/auth-context.tsx';
import { I18nProvider } from '../i18n/i18n.tsx';
import { ThemeProvider } from '../shared/theme/theme-context.tsx';
import { router } from './router.tsx';
import { SolidQueryDevtools } from '@tanstack/solid-query-devtools';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      retry: 1
    }
  }
});

export function AppProviders() {
  return (
    <ThemeProvider>
      <QueryClientProvider client={queryClient}>
        <I18nProvider>
          <AuthProvider>
            <RouterProvider router={router} />
          </AuthProvider>
        </I18nProvider>
        <SolidQueryDevtools />
      </QueryClientProvider>
    </ThemeProvider>
  );
}
