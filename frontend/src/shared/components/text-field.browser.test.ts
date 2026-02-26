import { render, screen } from "@solidjs/testing-library";
import { createComponent } from "solid-js";
import { describe, expect, it } from "vitest";

import { TextAreaField, TextField } from "./text-field";

describe("TextField", () => {
  it("renders an input and merges custom classes", () => {
    render(() =>
      createComponent(TextField, {
        class: "custom-input",
        value: "hello",
        "aria-label": "Title",
      }),
    );

    const input = screen.getByLabelText("Title");
    expect(input).toHaveValue("hello");
    expect(input).toHaveClass("custom-input");
    expect(input).toHaveClass("rounded-xl");
  });
});

describe("TextAreaField", () => {
  it("renders a textarea and merges custom classes", () => {
    render(() =>
      createComponent(TextAreaField, {
        class: "custom-textarea",
        "aria-label": "Description",
      }),
    );

    const textarea = screen.getByLabelText("Description");
    expect(textarea.tagName).toBe("TEXTAREA");
    expect(textarea).toHaveClass("custom-textarea");
    expect(textarea).toHaveClass("border-border-soft");
  });
});
