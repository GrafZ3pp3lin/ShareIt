import { Link } from "@tanstack/solid-router";
import { clsx } from "clsx/lite";
import type { ComponentProps, ParentProps } from "solid-js";
import { splitProps } from "solid-js";
import { getButtonClass, type ButtonVariant } from "./button";

type LinkButtonProps = ParentProps<
  Omit<ComponentProps<typeof Link>, "class"> & {
    variant?: ButtonVariant;
    class?: string;
  }
>;

export function LinkButton(props: LinkButtonProps) {
  const [local, rest] = splitProps(props, ["variant", "class", "children"]);
  const variant = () => local.variant ?? "primary";

  return (
    <Link {...rest} class={clsx(getButtonClass(variant()), local.class)}>
      {local.children}
    </Link>
  );
}
