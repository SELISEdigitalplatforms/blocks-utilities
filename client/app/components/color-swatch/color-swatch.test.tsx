import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { ColorSwatch, validHexaColorReg } from "./color-swatch";

describe("ColorSwatch", () => {
  it("renders the current value in the text input", () => {
    render(<ColorSwatch value="#aabbcc" />);
    expect(screen.getByPlaceholderText("#FFFFFF")).toHaveValue("#AABBCC");
  });

  it("sanitizes invalid characters from typed input", () => {
    const onChange = vi.fn();
    render(<ColorSwatch value="#" onChange={onChange} />);
    fireEvent.change(screen.getByPlaceholderText("#FFFFFF"), {
      target: { value: "#12zzGG34" },
    });
    // z is stripped, G kept (hex letters upper), single leading #.
    expect(onChange).toHaveBeenCalledWith("#1234");
  });

  it("propagates the native color picker value", () => {
    const onChange = vi.fn();
    const { container } = render(<ColorSwatch value="#000000" onChange={onChange} />);
    const colorInput = container.querySelector(
      'input[type="color"]',
    ) as HTMLInputElement;
    fireEvent.change(colorInput, { target: { value: "#ff0000" } });
    expect(onChange).toHaveBeenCalledWith("#FF0000");
  });

  it("exports a valid hex color regex", () => {
    expect(validHexaColorReg.test("#aabbcc")).toBe(true);
    expect(validHexaColorReg.test("nope")).toBe(false);
  });
});
