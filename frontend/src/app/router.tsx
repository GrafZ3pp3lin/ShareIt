import { createRouter } from '@solidjs/router';
import { routeTree } from '../shared/routes/route-tree.tsx';

// The instance is both the provider component to mount and the holder of the
// typed `paths` URL builder (`Router.paths`, inferred from `routeTree`).
export const Router = createRouter({ routes: routeTree });
