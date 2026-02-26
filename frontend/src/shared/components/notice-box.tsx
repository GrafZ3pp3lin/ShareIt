import { clsx } from 'clsx/lite';
import type { JSX, ParentProps } from 'solid-js';
import { splitProps } from 'solid-js';

type NoticeBoxProps = ParentProps<JSX.HTMLAttributes<HTMLDivElement>>;

export function NoticeBox(props: NoticeBoxProps) {
  const [local, rest] = splitProps(props, ['class', 'children']);

  return (
    <div
      {...rest}
      class={clsx(
        'rounded-xl border border-dashed border-border-soft bg-white px-3 py-3 text-sm text-muted',
        local.class
      )}
    >
      {local.children}
    </div>
  );
}
