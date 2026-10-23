// Local replacement for 'unplugin-icons/types/solid', which still imports its
// JSX types from 'solid-js' — removed in Solid 2.0 (JSX types now live in
// '@solidjs/web'). Keep in sync if unplugin-icons publishes v2-compatible types.
declare module 'virtual:icons/*' {
  import type { ComponentProps, JSX } from '@solidjs/web';

  const component: (props: ComponentProps<'svg'>) => JSX.Element;
  export default component;
}
declare module '~icons/*' {
  import type { ComponentProps, JSX } from '@solidjs/web';

  const component: (props: ComponentProps<'svg'>) => JSX.Element;
  export default component;
}
