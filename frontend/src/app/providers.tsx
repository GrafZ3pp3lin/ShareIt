import { QueryClient, QueryClientProvider } from '@tanstack/solid-query';
import { RouterProvider } from '@tanstack/solid-router';
import { AuthProvider } from '../features/auth/auth-context';
import { I18nProvider } from '../i18n';
import { configureApiClient } from '../shared/api/client';
import { router } from './router';
import { SolidQueryDevtools } from '@tanstack/solid-query-devtools';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      retry: 1,
    },
  },
});

configureApiClient();

export function AppProviders() {
  return (
    <QueryClientProvider client={queryClient}>
      <I18nProvider>
        <AuthProvider>
          <RouterProvider router={router} />
        </AuthProvider>
      </I18nProvider>
      <SolidQueryDevtools />
    </QueryClientProvider>
  );
}
