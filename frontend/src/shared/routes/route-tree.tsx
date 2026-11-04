import type { RouteSectionProps } from '@solidjs/router';
import { defineRoutes } from '@solidjs/router';
import { AppLayout } from '../../app/layout.tsx';
import { AccountPage } from '../../features/account/pages/account-page.tsx';
import { AuthCallbackPage } from '../../features/auth/pages/auth-callback-page.tsx';
import { NotFoundPage } from '../../features/not-found/pages/not-found-page.tsx';
import { SharePage } from '../../features/content/pages/share-page.tsx';
import { UploadPage } from '../../features/upload/pages/upload-page.tsx';

const AppShell = (props: RouteSectionProps) => <AppLayout {...props} variant="app" />;

const ShareShell = (props: RouteSectionProps) => <AppLayout {...props} variant="share" />;

const NotFoundShell = (props: RouteSectionProps) => (
  <AppLayout params={props.params} location={props.location} data={props.data} variant="share">
    <NotFoundPage />
  </AppLayout>
);

export const routeTree = defineRoutes([
  {
    path: '/',
    component: AppShell,
    children: [
      { path: '/', component: UploadPage },
      { path: '/auth/callback', component: AuthCallbackPage },
      // Temporary: the links feature lands in M5 and replaces this route's component.
      { path: '/links', component: AccountPage }
    ]
  },
  {
    path: '/s/:packageId',
    component: ShareShell,
    children: [{ path: '/', component: SharePage }]
  },
  {
    path: '*',
    component: NotFoundShell
  }
]);
