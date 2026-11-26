import type { RouteSectionProps } from '@solidjs/router';
import { defineRoutes } from '@solidjs/router';
import { AppLayout } from '../../app/layout.tsx';
import { LinksPage } from '../../features/links/pages/links-page.tsx';
import { AuthCallbackPage } from '../../features/auth/pages/auth-callback-page.tsx';
import { NotFoundPage } from '../../features/not-found/pages/not-found-page.tsx';
import { SharePage } from '../../features/share/pages/share-page.tsx';
import { ComposerPage } from '../../features/composer/pages/composer-page.tsx';
import { ImprintPage } from '../../features/imprint/pages/imprint-page.tsx';
import { PrivacyPage } from '../../features/privacy/pages/privacy-page.tsx';

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
      { path: '/', component: ComposerPage },
      { path: '/auth/callback', component: AuthCallbackPage },
      { path: '/links', component: LinksPage },
      { path: '/imprint', component: ImprintPage },
      { path: '/privacy', component: PrivacyPage }
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
