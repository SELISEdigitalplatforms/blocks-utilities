import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { GenerateTokenModal } from "./generate-pat-modal";

const generateToken = vi.fn();
let isPending = false;
let isError = false;
vi.mock("@/idp/iam/hooks/use-activity", () => ({
  useGeneratePats: () => ({ mutate: generateToken, isPending, isError }),
}));

describe("GenerateTokenModal", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isPending = false;
    isError = false;
  });

  it("renders the fields with the default expiration", () => {
    render(<GenerateTokenModal isOpen onClose={() => {}} id="u1" />);
    expect(screen.getByText("Generate Token")).toBeInTheDocument();
    expect(screen.getByLabelText(/PAT Name/)).toBeInTheDocument();
    expect(screen.getByText(/30 days/)).toBeInTheDocument();
  });

  it("keeps Generate disabled until a name is entered", () => {
    render(<GenerateTokenModal isOpen onClose={() => {}} id="u1" />);
    expect(screen.getByRole("button", { name: "Generate" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/PAT Name/), {
      target: { value: "My token" },
    });
    expect(screen.getByRole("button", { name: "Generate" })).toBeEnabled();
  });

  it("generates a token and reports success", async () => {
    const onSuccess = vi.fn();
    const onClose = vi.fn();
    generateToken.mockImplementation((_payload, opts) => opts.onSuccess({ token: "t" }));
    render(
      <GenerateTokenModal isOpen onClose={onClose} id="u1" onSuccess={onSuccess} />,
    );
    fireEvent.change(screen.getByLabelText(/PAT Name/), {
      target: { value: "My token" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Generate" }));
    await waitFor(() => expect(generateToken).toHaveBeenCalled());
    const payload = generateToken.mock.calls[0][0];
    expect(payload.note).toBe("My token");
    expect(payload.codeTtlInMinute).toBe(30 * 24 * 60);
    expect(onSuccess).toHaveBeenCalledWith({ token: "t" });
    expect(onClose).toHaveBeenCalled();
  });

  it("shows an error banner when the mutation errored", () => {
    isError = true;
    render(<GenerateTokenModal isOpen onClose={() => {}} id="u1" />);
    expect(
      screen.getByText("Failed to generate token. Please try again."),
    ).toBeInTheDocument();
  });

  it("closes and resets on Cancel", () => {
    const onClose = vi.fn();
    render(<GenerateTokenModal isOpen onClose={onClose} id="u1" />);
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onClose).toHaveBeenCalled();
  });
});
