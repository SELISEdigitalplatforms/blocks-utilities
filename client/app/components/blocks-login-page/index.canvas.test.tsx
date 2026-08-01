import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render } from "@testing-library/react";

import { BlocksLoginPage } from "./index";
import { BLOCKS_PRODUCTS } from "@/constants/blocks-products";

/**
 * jsdom returns null from canvas.getContext, so the atmospheric-canvas effect
 * normally bails out before running any drawing code. These tests supply a
 * stub 2D context and a controllable requestAnimationFrame so the resize and
 * draw paths execute exactly once.
 */
describe("BlocksLoginPage canvas animation", () => {
  const product = BLOCKS_PRODUCTS[0];
  let frameCb: FrameRequestCallback | null = null;
  const gradient = { addColorStop: vi.fn() };
  const ctx = {
    setTransform: vi.fn(),
    clearRect: vi.fn(),
    createRadialGradient: vi.fn(() => gradient),
    fillRect: vi.fn(),
    fillStyle: "",
  };
  let getContextSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    frameCb = null;
    vi.clearAllMocks();
    getContextSpy = vi
      .spyOn(HTMLCanvasElement.prototype, "getContext")
      .mockReturnValue(ctx as unknown as CanvasRenderingContext2D);
    vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => {
      frameCb = cb;
      return 1;
    });
    vi.stubGlobal("cancelAnimationFrame", () => {});
  });

  afterEach(() => {
    getContextSpy.mockRestore();
    vi.unstubAllGlobals();
  });

  it("resizes and draws one animation frame using the 2D context", () => {
    render(<BlocksLoginPage name={product.name} onLogin={() => {}} />);

    // The effect calls resize() (setTransform) and schedules the first frame.
    expect(ctx.setTransform).toHaveBeenCalled();
    expect(typeof frameCb).toBe("function");

    // Drive exactly one frame; draw() paints three radial gradients.
    frameCb?.(0);
    expect(ctx.clearRect).toHaveBeenCalled();
    expect(ctx.createRadialGradient).toHaveBeenCalledTimes(3);
    expect(gradient.addColorStop).toHaveBeenCalled();
    expect(ctx.fillRect).toHaveBeenCalled();
  });

  it("repaints when the window is resized", () => {
    render(<BlocksLoginPage name={product.name} onLogin={() => {}} />);
    ctx.setTransform.mockClear();
    window.dispatchEvent(new Event("resize"));
    expect(ctx.setTransform).toHaveBeenCalled();
  });
});
