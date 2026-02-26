import { clsx } from 'clsx/lite';
import type { JSX, ParentProps } from 'solid-js';
import { splitProps } from 'solid-js';
import { Dynamic } from 'solid-js/web';

type PillBadgeProps = ParentProps<
  JSX.HTMLAttributes<HTMLElement> & {
    as?: 'div' | 'span' | 'label';
  }
>;

export function PillBadge(props: PillBadgeProps) {
  const [local, rest] = splitProps(props, ['as', 'class', 'children']);

  return (
    <Dynamic
      component={local.as ?? 'div'}
      {...rest}
      class={clsx(
        'inline-flex items-center gap-2 rounded-full border border-border-soft bg-panel-soft px-3 py-1 text-xs font-medium text-muted',
        local.class
      )}
    >
      {local.children}
    </Dynamic>
  );
}
