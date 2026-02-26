import { describe, expect, it } from "vitest";

import { getButtonClass } from "./button";

describe("getButtonClass", () => {
  it("returns primary classes by default", () => {
    const className = getButtonClass();

    expect(className).toContain("inline-flex");
    expect(className).toContain("bg-brand");
    expect(className).toContain("text-white");
  });

  it("returns variant-specific classes", () => {
    expect(getButtonClass("secondary")).toContain("border-border-soft");
    expect(getButtonClass("ghost")).toContain("bg-transparent");
  });
});
