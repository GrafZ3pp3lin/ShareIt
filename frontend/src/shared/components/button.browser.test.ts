import { fireEvent, render, screen } from "@solidjs/testing-library";
import { createComponent } from "solid-js";
import { describe, expect, it, vi } from "vitest";

import { Button } from "./button";

describe("Button", () => {
  it("renders children and uses the primary variant by default", () => {
    render(() => createComponent(Button, { children: "Save" }));

    const button = screen.getByRole("button", { name: "Save" });
    expect(button).toBeInTheDocument();
    expect(button).toHaveClass("inline-flex");
    expect(button).toHaveClass("bg-brand");
  });

  it("applies custom classes and forwards button attributes", () => {
    render(() =>
      createComponent(Button, {
        children: "Submit",
        class: "custom-class",
        type: "submit",
        disabled: true,
        variant: "secondary",
      }),
    );

    const button = screen.getByRole("button", { name: "Submit" });
    expect(button).toHaveClass("custom-class");
    expect(button).toHaveClass("border-border-soft");
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute("type", "submit");
  });

  it("forwards click handlers", async () => {
    const onClick = vi.fn();
    render(() => createComponent(Button, { children: "Click me", onClick }));

    await fireEvent.click(screen.getByRole("button", { name: "Click me" }));
    expect(onClick).toHaveBeenCalledTimes(1);
  });
});
