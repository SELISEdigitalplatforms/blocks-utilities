import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { UrlWithActions } from "./url-with-actions";

describe("UrlWithActions", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useProjectStore.setState({ selectedProject: { tenantId: "tg1" } });
  });
  afterEach(() => vi.unstubAllGlobals());

  it("renders the certificate label with the jwks url in the title", () => {
    render(<UrlWithActions url="https://cdn/cert.pem" />);
    const label = screen.getByText("certificate");
    expect(label).toHaveAttribute("title", expect.stringContaining("X-Blocks-Key=tg1"));
  });

  it("copies the jwks url to the clipboard", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", {
      configurable: true,
      value: { writeText },
    });
    Object.defineProperty(window, "isSecureContext", {
      configurable: true,
      value: true,
    });
    render(<UrlWithActions url="https://cdn/cert.pem" />);
    fireEvent.click(screen.getByRole("button", { name: "Copy URL" }));
    await waitFor(() =>
      expect(writeText).toHaveBeenCalledWith(
        expect.stringContaining("X-Blocks-Key=tg1"),
      ),
    );
  });

  it("downloads the certificate through a blob url", async () => {
    const blob = new Blob(["pem"]);
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({ blob: () => Promise.resolve(blob) }),
    );
    const createObjectURL = vi.fn(() => "blob:dl");
    const revokeObjectURL = vi.fn();
    globalThis.URL.createObjectURL = createObjectURL;
    globalThis.URL.revokeObjectURL = revokeObjectURL;
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});

    render(<UrlWithActions url="https://cdn/cert.pem" />);
    fireEvent.click(screen.getByRole("button", { name: "Download certificate" }));
    await waitFor(() => expect(createObjectURL).toHaveBeenCalledWith(blob));
    expect(clickSpy).toHaveBeenCalled();
    expect(revokeObjectURL).toHaveBeenCalledWith("blob:dl");
    clickSpy.mockRestore();
  });
});
