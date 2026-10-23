import { defineRoutes } from '@solidjs/router';
import { AppLayout } from '../../app/layout.tsx';
import { AuthCallbackPage } from '../../features/auth/pages/auth-callback-page.tsx';
import { AccountPage } from '../../features/account/pages/account-page.tsx';
import { SharePage } from '../../features/content/pages/share-page.tsx';
import { UploadPage } from '../../features/upload/pages/upload-page.tsx';

export const routeTree = defineRoutes([
  {
    path: '/',
    component: AppLayout,
    children: [
      { path: '/', component: UploadPage },
      { path: '/auth/callback', component: AuthCallbackPage },
      { path: '/s/:packageId', component: SharePage },
      { path: '/account', component: AccountPage }
    ]
  }
]);
