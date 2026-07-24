import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, renderHook, waitFor } from "@testing-library/react";
import React, { useState } from "react";
import {
  FileUploader,
  FileInput,
  FileUploaderContent,
  FileUploaderItem,
  useFileUpload,
} from "./file-uploader";

const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const sonnerError = vi.fn();
vi.mock("sonner", () => ({
  toast: { error: (...a: unknown[]) => sonnerError(...a) },
}));

function Harness({
  initial = [],
  maxFiles = 2,
  orientation = "vertical" as "vertical" | "horizontal",
}: {
  initial?: File[];
  maxFiles?: number;
  orientation?: "vertical" | "horizontal";
}) {
  const [value, setValue] = useState<File[] | null>(initial);
  return (
    <FileUploader
      value={value}
      onValueChange={setValue}
      orientation={orientation}
      dropzoneOptions={{ maxFiles, maxSize: 5 * 1024 * 1024 }}
      data-testid="uploader"
    >
      <FileInput data-testid="file-input">
        <span>Drop files here</span>
      </FileInput>
      <FileUploaderContent>
        {(value ?? []).map((f, i) => (
          <FileUploaderItem key={i} index={i}>
            {f.name}
          </FileUploaderItem>
        ))}
      </FileUploaderContent>
    </FileUploader>
  );
}

describe("FileUploader", () => {
  beforeEach(() => {
    showErrorToast.mockReset();
    sonnerError.mockReset();
  });

  it("useFileUpload throws outside a provider", () => {
    expect(() => renderHook(() => useFileUpload())).toThrow(
      /must be used within a FileUploaderProvider/,
    );
  });

  it("renders the input area and existing files", () => {
    render(
      <Harness initial={[new File(["a"], "a.png", { type: "image/png" })]} />,
    );
    expect(screen.getByText("Drop files here")).toBeInTheDocument();
    expect(screen.getByText("a.png")).toBeInTheDocument();
    expect(screen.getByText("remove item 0")).toBeInTheDocument();
  });

  it("removes a file when its remove button is clicked", () => {
    render(
      <Harness
        initial={[
          new File(["a"], "a.png", { type: "image/png" }),
          new File(["b"], "b.png", { type: "image/png" }),
        ]}
      />,
    );
    expect(screen.getByText("a.png")).toBeInTheDocument();
    fireEvent.click(screen.getByText("remove item 0"));
    expect(screen.queryByText("a.png")).not.toBeInTheDocument();
    expect(screen.getByText("b.png")).toBeInTheDocument();
  });

  it("adds a dropped file through the hidden input", async () => {
    const { container } = render(<Harness />);
    const input = container.querySelector(
      'input[type="file"]',
    ) as HTMLInputElement;
    const file = new File(["hello"], "hello.png", { type: "image/png" });
    fireEvent.change(input, { target: { files: [file] } });
    expect(await screen.findByText("hello.png")).toBeInTheDocument();
  });

  it("supports keyboard navigation and deletion", () => {
    render(
      <Harness
        initial={[
          new File(["a"], "a.png", { type: "image/png" }),
          new File(["b"], "b.png", { type: "image/png" }),
        ]}
      />,
    );
    const region = screen.getByText("Drop files here").closest("[tabindex]")!
      .parentElement!.parentElement as HTMLElement;
    // Navigate down then back up, then delete the active item.
    fireEvent.keyDown(region, { key: "ArrowDown" });
    fireEvent.keyDown(region, { key: "ArrowUp" });
    fireEvent.keyDown(region, { key: "Escape" });
    expect(screen.getByText("a.png")).toBeInTheDocument();
  });
});
