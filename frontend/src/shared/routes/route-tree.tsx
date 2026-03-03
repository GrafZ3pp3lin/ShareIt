import { createRootRoute, createRoute } from '@tanstack/solid-router';
import { AppLayout } from '../../app/layout';
import { AuthCallbackPage } from '../../features/auth/pages/auth-callback-page';
import { AccountPage } from '../../features/account/pages/account-page';
import { SharePage } from '../../features/content/pages/share-page';
import { UploadPage } from '../../features/upload/pages/upload-page';

const rootRoute = createRootRoute({
  component: AppLayout,
});

const indexRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  component: UploadPage,
});

const authCallbackRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/auth/callback',
  component: AuthCallbackPage,
});

const shareRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/s/$packageId',
  component: SharePage,
});

const accountRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/account',
  component: AccountPage,
});

export const routeTree = rootRoute.addChildren([indexRoute, authCallbackRoute, shareRoute, accountRoute]);
