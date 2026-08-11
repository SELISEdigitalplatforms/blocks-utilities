import { describe, it, expect, vi, beforeEach } from "vitest";
import {
  render,
  screen,
  fireEvent,
  waitFor,
  createEvent,
} from "@testing-library/react";
import React from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MagicUrlConfigDialog } from "./magic-url-config-dialog";

let configResult: { data: unknown; isLoading: boolean } = {
  data: undefined,
  isLoading: false,
};

vi.mock("@blocks-utilities/magic-url/hooks/use-magic-url", () => ({
  useGetMagicUrlConfig: () => configResult,
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const wrap = (ui: React.ReactElement) =>
  render(
    <QueryClientProvider client={new QueryClient()}>{ui}</QueryClientProvider>,
  );

describe("MagicUrlConfigDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    configResult = { data: undefined, isLoading: false };
  });

  it("shows a spinner while the config loads", () => {
    configResult = { data: undefined, isLoading: true };
    wrap(<MagicUrlConfigDialog open onOpenChange={() => {}} projectKey="pk" />);
    expect(document.querySelector(".animate-spin")).toBeTruthy();
  });

  it("prefills the form from an existing config", () => {
    configResult = {
      data: { config: { contextName: "Ctx", shortUrlBase: "https://s.io/" } },
      isLoading: false,
    };
    wrap(
      <MagicUrlConfigDialog open onOpenChange={() => {}} projectKey="pk" />,
    );
    expect(screen.getByLabelText(/Context Name/)).toHaveValue("Ctx");
    expect(screen.getByLabelText(/Short URL Base/)).toHaveValue("https://s.io/");
  });

  it("defaults the fields when no config exists", () => {
    configResult = { data: { config: null }, isLoading: false };
    wrap(
      <MagicUrlConfigDialog open onOpenChange={() => {}} projectKey="pk" />,
    );
    expect(screen.getByLabelText(/Context Name/)).toHaveValue("Default");
  });

  it("validates required and malformed fields", async () => {
    configResult = {
      data: { config: { contextName: "", shortUrlBase: "" } },
      isLoading: false,
    };
    wrap(
      <MagicUrlConfigDialog open onOpenChange={() => {}} projectKey="pk" />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(await screen.findByText("Context name is required")).toBeInTheDocument();
    expect(screen.getByText("Short URL base is required")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/Context Name/), {
      target: { value: "Ctx" },
    });
    fireEvent.change(screen.getByLabelText(/Short URL Base/), {
      target: { value: "not a url" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(
      await screen.findByText("URL must be a valid HTTPS/HTTP URL"),
    ).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/Short URL Base/), {
      target: { value: "https://short.io/path" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(
      await screen.findByText("URL must end with a forward slash (/)"),
    ).toBeInTheDocument();
  });

  it("saves and shows a success toast when the config call succeeds", async () => {
    const onSave = vi.fn().mockResolvedValue(undefined);
    const onOpenChange = vi.fn();
    configResult = {
      data: {
        config: { contextName: "Ctx", shortUrlBase: "https://short.io/" },
        isSuccess: true,
      },
      isLoading: false,
    };
    wrap(
      <MagicUrlConfigDialog
        open
        onOpenChange={onOpenChange}
        projectKey="pk"
        onSave={onSave}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(onSave).toHaveBeenCalled());
    expect(showSuccessToast).toHaveBeenCalled();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("shows an error toast when the config call was not successful", async () => {
    const onSave = vi.fn().mockResolvedValue(undefined);
    configResult = {
      data: {
        config: { contextName: "Ctx", shortUrlBase: "https://short.io/" },
        isSuccess: false,
        errorMessage: "bad",
      },
      isLoading: false,
    };
    wrap(
      <MagicUrlConfigDialog open onOpenChange={() => {}} projectKey="pk" onSave={onSave} />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(showErrorToast).toHaveBeenCalled());
  });

  describe("trigger wrapper", () => {
    const renderTrigger = (onOpenChange = vi.fn()) => {
      wrap(
        <MagicUrlConfigDialog
          open={false}
          onOpenChange={onOpenChange}
          projectKey="pk"
          trigger={<button type="button">Configure</button>}
        />,
      );
      const controls = screen.getAllByRole("button", { name: "Configure" });
      return {
        onOpenChange,
        wrapper: controls.find((el) => el.tagName === "DIV") as HTMLElement,
        nested: controls.find((el) => el.tagName === "BUTTON") as HTMLElement,
      };
    };

    it("opens the dialog when the wrapper is clicked", () => {
      const { onOpenChange, wrapper } = renderTrigger();
      fireEvent.click(wrapper);
      expect(onOpenChange).toHaveBeenCalledWith(true);
    });

    it("opens the dialog when Enter is pressed on the wrapper", () => {
      const { onOpenChange, wrapper } = renderTrigger();
      fireEvent.keyDown(wrapper, { key: "Enter" });
      expect(onOpenChange).toHaveBeenCalledWith(true);
    });

    it("opens the dialog on Space and prevents the page from scrolling", () => {
      const { onOpenChange, wrapper } = renderTrigger();
      const event = createEvent.keyDown(wrapper, { key: " " });
      fireEvent(wrapper, event);
      expect(onOpenChange).toHaveBeenCalledWith(true);
      expect(event.defaultPrevented).toBe(true);
    });

    it("ignores keys other than Enter and Space", () => {
      const { onOpenChange, wrapper } = renderTrigger();
      fireEvent.keyDown(wrapper, { key: "Escape" });
      expect(onOpenChange).not.toHaveBeenCalled();
    });

    it("ignores keys already handled by the nested trigger control", () => {
      const { onOpenChange, nested } = renderTrigger();
      fireEvent.keyDown(nested, { key: "Enter" });
      expect(onOpenChange).not.toHaveBeenCalled();
    });
  });

  it("does nothing when no projectKey is provided", () => {
    wrap(<MagicUrlConfigDialog open onOpenChange={() => {}} />);
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(showSuccessToast).not.toHaveBeenCalled();
    expect(showErrorToast).not.toHaveBeenCalled();
  });
});
