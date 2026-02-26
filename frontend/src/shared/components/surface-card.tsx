import { clsx } from 'clsx/lite';
import type { JSX, ParentProps } from 'solid-js';
import { splitProps } from 'solid-js';

type SurfaceCardProps = ParentProps<JSX.HTMLAttributes<HTMLDivElement>>;

export function SurfaceCard(props: SurfaceCardProps) {
  const [local, rest] = splitProps(props, ['class', 'children']);

  return (
    <div {...rest} class={clsx('rounded-2xl border border-border-soft bg-white p-5 shadow-card', local.class)}>
      {local.children}
    </div>
  );
}
