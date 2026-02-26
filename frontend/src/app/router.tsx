import { createRouter } from '@tanstack/solid-router';
import { routeTree } from '../shared/routes/route-tree';

export const router = createRouter({
  routeTree,
  defaultPreload: 'intent',
});

declare module '@tanstack/solid-router' {
  interface Register {
    router: typeof router;
  }
}
