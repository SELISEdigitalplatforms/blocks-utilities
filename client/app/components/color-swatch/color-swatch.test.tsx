import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent, createEvent } from "@testing-library/react";
import { ColorSwatch, validHexaColorReg } from "./color-swatch";

const renderSwatch = () => {
  const { container } = render(<ColorSwatch value="#000000" />);
  const colorInput = container.querySelector(
    'input[type="color"]',
  ) as HTMLInputElement;
  const clickSpy = vi.spyOn(colorInput, "click").mockImplementation(() => {});
  return {
    swatch: screen.getByRole("button", { name: "Pick a color" }),
    colorInput,
    clickSpy,
  };
};

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

  it("opens the color picker when the swatch is clicked", () => {
    const { swatch, clickSpy } = renderSwatch();
    fireEvent.click(swatch);
    expect(clickSpy).toHaveBeenCalledTimes(1);
  });

  it("opens the color picker when Enter is pressed on the swatch", () => {
    const { swatch, clickSpy } = renderSwatch();
    fireEvent.keyDown(swatch, { key: "Enter" });
    expect(clickSpy).toHaveBeenCalledTimes(1);
  });

  it("opens the color picker on Space and prevents the page from scrolling", () => {
    const { swatch, clickSpy } = renderSwatch();
    const event = createEvent.keyDown(swatch, { key: " " });
    fireEvent(swatch, event);
    expect(clickSpy).toHaveBeenCalledTimes(1);
    expect(event.defaultPrevented).toBe(true);
  });

  it("ignores keys other than Enter and Space", () => {
    const { swatch, clickSpy } = renderSwatch();
    fireEvent.keyDown(swatch, { key: "a" });
    fireEvent.keyDown(swatch, { key: "Escape" });
    expect(clickSpy).not.toHaveBeenCalled();
  });

  it("ignores keys raised by the nested color input so it does not re-open itself", () => {
    const { colorInput, clickSpy } = renderSwatch();
    fireEvent.keyDown(colorInput, { key: "Enter" });
    expect(clickSpy).not.toHaveBeenCalled();
  });

  it("exports a valid hex color regex", () => {
    expect(validHexaColorReg.test("#aabbcc")).toBe(true);
    expect(validHexaColorReg.test("nope")).toBe(false);
  });
});
