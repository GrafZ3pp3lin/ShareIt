import { clsx } from 'clsx/lite';
import type { JSX, ParentProps } from 'solid-js';
import { splitProps } from 'solid-js';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost';

type ButtonProps = ParentProps<
  JSX.ButtonHTMLAttributes<HTMLButtonElement> & {
    variant?: ButtonVariant;
  }
>;

const baseClass =
  'inline-flex items-center justify-center gap-2 rounded-lg border border-transparent px-4 py-2 text-sm font-semibold tracking-wide transition duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ink/25';

const variantClasses: Record<ButtonVariant, string> = {
  primary:
    'bg-brand text-white shadow-button hover:bg-brand-hover disabled:cursor-not-allowed disabled:opacity-60 disabled:shadow-none',
  secondary:
    'border-border-soft bg-white text-ink shadow-button-soft hover:shadow-button-soft-hover disabled:cursor-not-allowed disabled:opacity-60 disabled:shadow-none',
  ghost: 'bg-transparent text-muted hover:bg-panel-soft hover:text-ink',
};

export function Button(props: ButtonProps) {
  const [local, rest] = splitProps(props, ['variant', 'class', 'children']);
  const variant = () => local.variant ?? 'primary';

  return (
    <button {...rest} class={clsx(baseClass, variantClasses[variant()], local.class)}>
      {local.children}
    </button>
  );
}

export function getButtonClass(variant: ButtonVariant = 'primary') {
  return clsx(baseClass, variantClasses[variant]);
}
