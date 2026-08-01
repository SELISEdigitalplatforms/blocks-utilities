import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MagicUrlDialog } from "./magic-url-dialog";

const createMagicUrl = vi.fn();
let isPending = false;

vi.mock("@blocks-utilities/magic-url/hooks/use-magic-url", () => ({
  useCreateMagicUrl: () => ({ mutate: createMagicUrl, isPending }),
}));

const toast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  toast: (...args: unknown[]) => toast(...args),
}));

describe("MagicUrlDialog", () => {
  beforeEach(() => {
    createMagicUrl.mockReset();
    toast.mockReset();
    isPending = false;
  });

  it("renders the dialog content when open", () => {
    render(<MagicUrlDialog open onOpenChange={() => {}} />);
    expect(screen.getByText("Magic URL")).toBeInTheDocument();
    expect(
      screen.getByText("Create a new Magic URL with custom configurations."),
    ).toBeInTheDocument();
    expect(screen.getByLabelText("URI *")).toBeInTheDocument();
    expect(screen.getByLabelText("Name *")).toBeInTheDocument();
  });

  it("keeps the Create button disabled while the form is invalid", () => {
    render(<MagicUrlDialog open onOpenChange={() => {}} />);
    const create = screen.getByRole("button", { name: "Create" });
    expect(create).toBeDisabled();
  });

  it("submits a valid payload and shows a success toast", async () => {
    createMagicUrl.mockImplementation((_payload, opts) => opts.onSuccess());
    const onOpenChange = vi.fn();
    render(<MagicUrlDialog open onOpenChange={onOpenChange} />);

    fireEvent.change(screen.getByLabelText("URI *"), {
      target: { value: "https://example.com" },
    });
    fireEvent.change(screen.getByLabelText("Name *"), {
      target: { value: "My Link" },
    });

    const create = screen.getByRole("button", { name: "Create" });
    await waitFor(() => expect(create).toBeEnabled());
    fireEvent.click(create);

    expect(createMagicUrl).toHaveBeenCalledTimes(1);
    const payload = createMagicUrl.mock.calls[0][0];
    expect(payload.uri).toBe("https://example.com");
    expect(payload.name).toBe("My Link");
    expect(payload.type).toBe(1);
    expect(toast).toHaveBeenCalledWith(
      expect.objectContaining({ variant: "success" }),
    );
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("reports an error toast when the mutation fails", async () => {
    createMagicUrl.mockImplementation((_payload, opts) =>
      opts.onError(new Error("boom")),
    );
    render(<MagicUrlDialog open onOpenChange={() => {}} />);

    fireEvent.change(screen.getByLabelText("URI *"), {
      target: { value: "https://example.com" },
    });
    fireEvent.change(screen.getByLabelText("Name *"), {
      target: { value: "My Link" },
    });
    const create = screen.getByRole("button", { name: "Create" });
    await waitFor(() => expect(create).toBeEnabled());
    fireEvent.click(create);

    expect(toast).toHaveBeenCalledWith(
      expect.objectContaining({ variant: "destructive", description: "boom" }),
    );
  });

  it("closes without submitting when Cancel is clicked", () => {
    const onOpenChange = vi.fn();
    render(<MagicUrlDialog open onOpenChange={onOpenChange} />);
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(createMagicUrl).not.toHaveBeenCalled();
  });

  it("prefills fields from initialData", () => {
    render(
      <MagicUrlDialog
        open
        onOpenChange={() => {}}
        initialData={
          {
            uri: "https://seed.example",
            name: "Seeded",
            type: "1",
            requestMethod: "GET",
            usageLimit: 0,
          } as never
        }
      />,
    );
    expect(screen.getByLabelText("URI *")).toHaveValue("https://seed.example");
    expect(screen.getByLabelText("Name *")).toHaveValue("Seeded");
  });
});
