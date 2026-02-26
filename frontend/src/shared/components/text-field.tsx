import { clsx } from 'clsx/lite';
import type { JSX } from 'solid-js';

export function TextField(props: JSX.InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      {...props}
      class={clsx('w-full rounded-xl border border-border-soft bg-white px-3 py-2 text-sm', props.class)}
    />
  );
}

export function TextAreaField(props: JSX.TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return (
    <textarea
      {...props}
      class={clsx('w-full rounded-xl border border-border-soft bg-white px-3 py-2 text-sm', props.class)}
    />
  );
}
