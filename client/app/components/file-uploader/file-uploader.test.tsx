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
  dropzoneOptions,
}: {
  initial?: File[];
  maxFiles?: number;
  orientation?: "vertical" | "horizontal";
  dropzoneOptions?: Record<string, unknown>;
}) {
  const [value, setValue] = useState<File[] | null>(initial);
  return (
    <FileUploader
      value={value}
      onValueChange={setValue}
      orientation={orientation}
      dropzoneOptions={
        (dropzoneOptions as never) ?? { maxFiles, maxSize: 5 * 1024 * 1024 }
      }
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
    const region = screen.getByTestId("uploader");
    // Navigate down (activate index 0), wrap around, back up, then escape.
    fireEvent.keyDown(region, { key: "ArrowDown" });
    fireEvent.keyDown(region, { key: "ArrowDown" });
    fireEvent.keyDown(region, { key: "ArrowDown" });
    fireEvent.keyDown(region, { key: "ArrowUp" });
    fireEvent.keyDown(region, { key: "Escape" });
    expect(screen.getByText("a.png")).toBeInTheDocument();
  });

  it("clicks the hidden input when Enter is pressed with no active item", () => {
    const { container } = render(<Harness />);
    const input = container.querySelector(
      'input[type="file"]',
    ) as HTMLInputElement;
    const clickSpy = vi.spyOn(input, "click");
    fireEvent.keyDown(screen.getByTestId("uploader"), { key: "Enter" });
    expect(clickSpy).toHaveBeenCalled();
  });

  it("deletes the active item with the Delete key", () => {
    render(
      <Harness
        initial={[
          new File(["a"], "a.png", { type: "image/png" }),
          new File(["b"], "b.png", { type: "image/png" }),
        ]}
      />,
    );
    const region = screen.getByTestId("uploader");
    fireEvent.keyDown(region, { key: "ArrowDown" });
    fireEvent.keyDown(region, { key: "Delete" });
    expect(screen.queryByText("a.png")).not.toBeInTheDocument();
  });

  it("resets the active index after deleting the last remaining item", () => {
    render(
      <Harness initial={[new File(["a"], "a.png", { type: "image/png" })]} />,
    );
    const region = screen.getByTestId("uploader");
    fireEvent.keyDown(region, { key: "ArrowDown" });
    fireEvent.keyDown(region, { key: "Backspace" });
    expect(screen.queryByText("a.png")).not.toBeInTheDocument();
  });

  it("navigates horizontally with left and right arrows", () => {
    render(
      <Harness
        orientation="horizontal"
        initial={[
          new File(["a"], "a.png", { type: "image/png" }),
          new File(["b"], "b.png", { type: "image/png" }),
        ]}
      />,
    );
    const region = screen.getByTestId("uploader");
    fireEvent.keyDown(region, { key: "ArrowRight" });
    fireEvent.keyDown(region, { key: "ArrowLeft" });
    expect(screen.getByText("a.png")).toBeInTheDocument();
  });

  it("replaces the current file when maxFiles is 1 (reSelect)", async () => {
    render(
      <Harness
        maxFiles={1}
        initial={[new File(["a"], "a.png", { type: "image/png" })]}
      />,
    );
    const input = screen
      .getByTestId("uploader")
      .querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File(["z"], "z.png", { type: "image/png" });
    fireEvent.change(input, { target: { files: [file] } });
    expect(await screen.findByText("z.png")).toBeInTheDocument();
    expect(screen.queryByText("a.png")).not.toBeInTheDocument();
  });

  it("reports an invalid file type through the error toast", async () => {
    render(
      <Harness
        dropzoneOptions={{
          maxFiles: 1,
          multiple: false,
          maxSize: 5 * 1024 * 1024,
          accept: { "image/png": [".png"] },
        }}
      />,
    );
    const input = screen
      .getByTestId("uploader")
      .querySelector('input[type="file"]') as HTMLInputElement;
    const bad = new File(["hi"], "notes.txt", { type: "text/plain" });
    fireEvent.change(input, { target: { files: [bad] } });
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "Invalid file type" }),
    );
  });

  it("reports a too-large file through the error toast", async () => {
    render(
      <Harness
        dropzoneOptions={{
          maxFiles: 1,
          multiple: false,
          maxSize: 3,
          accept: { "image/png": [".png"] },
        }}
      />,
    );
    const input = screen
      .getByTestId("uploader")
      .querySelector('input[type="file"]') as HTMLInputElement;
    const big = new File(["way too many bytes"], "big.png", {
      type: "image/png",
    });
    fireEvent.change(input, { target: { files: [big] } });
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith(
        expect.objectContaining({ errors: expect.stringContaining("too large") }),
      ),
    );
  });
});
