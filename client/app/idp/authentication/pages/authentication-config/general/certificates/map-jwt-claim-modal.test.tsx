import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { useProjectStore } from "@seliseblocks/genesis-os";
import MapJwtClaimModal from "./map-jwt-claim-modal";

const saveJWTClaim = vi.fn();
let isLoading = false;
let existingJwtClaim: Record<string, unknown> | undefined;
let isJwtClaimLoading = false;

vi.mock("@blocks-idp/authentication/hooks/use-jwt-claim", () => ({
  useAddJwtClaim: () => ({ mutateAsync: saveJWTClaim, isPending: isLoading }),
  useGetJwtClaim: () => ({ data: existingJwtClaim, isLoading: isJwtClaimLoading }),
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const b64url = (obj: unknown) =>
  btoa(JSON.stringify(obj)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
const makeJwt = (payload: unknown) =>
  `${b64url({ alg: "HS256", typ: "JWT" })}.${b64url(payload)}.signature`;

describe("MapJwtClaimModal", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isLoading = false;
    isJwtClaimLoading = false;
    existingJwtClaim = undefined;
    useProjectStore.setState({ selectedProject: { tenantId: "tg1" } });
  });

  it("renders the drawer with the JWT input", () => {
    render(<MapJwtClaimModal open onOpenChange={() => {}} />);
    expect(screen.getByText("Map JWT Claim")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Paste here...")).toBeInTheDocument();
    expect(
      screen.getByText(/Please paste a valid JWT above/),
    ).toBeInTheDocument();
  });

  it("validates an empty token on decode", () => {
    render(<MapJwtClaimModal open onOpenChange={() => {}} />);
    fireEvent.click(screen.getByRole("button", { name: "Decode" }));
    expect(screen.getByText("JWT is required.")).toBeInTheDocument();
  });

  it("flags an invalid token", () => {
    render(<MapJwtClaimModal open onOpenChange={() => {}} />);
    fireEvent.change(screen.getByPlaceholderText("Paste here..."), {
      target: { value: "not-a-jwt" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Decode" }));
    expect(screen.getByText("Invalid JWT Token.")).toBeInTheDocument();
  });

  it("decodes a valid token and reveals the mapping table", () => {
    render(<MapJwtClaimModal open onOpenChange={() => {}} />);
    fireEvent.change(screen.getByPlaceholderText("Paste here..."), {
      target: { value: makeJwt({ sub: "u1", email: "a@b.co", name: "Ada" }) },
    });
    fireEvent.click(screen.getByRole("button", { name: "Decode" }));
    expect(showSuccessToast).toHaveBeenCalled();
    expect(screen.getByText("JWT Key")).toBeInTheDocument();
    expect(screen.getByText("User Id")).toBeInTheDocument();
  });

  it("shows the loading skeleton while the existing claim loads", () => {
    isJwtClaimLoading = true;
    render(<MapJwtClaimModal open onOpenChange={() => {}} />);
    expect(document.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  });

  it("saves when editing existing mapped data", async () => {
    existingJwtClaim = { itemId: "j1", userId: "sub", email: "email", name: "", userName: "", roles: "" };
    saveJWTClaim.mockResolvedValue({ isSuccess: true });
    const onOpenChange = vi.fn();
    render(<MapJwtClaimModal open onOpenChange={onOpenChange} />);
    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).toBeEnabled());
    fireEvent.click(save);
    await waitFor(() => expect(saveJWTClaim).toHaveBeenCalled());
    expect(saveJWTClaim).toHaveBeenCalledWith(
      expect.objectContaining({ itemId: "j1", projectKey: "tg1" }),
    );
    expect(showSuccessToast).toHaveBeenCalled();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("reports an error when the save is unsuccessful", async () => {
    existingJwtClaim = { itemId: "j1", userId: "sub" };
    saveJWTClaim.mockResolvedValue({ isSuccess: false });
    render(<MapJwtClaimModal open onOpenChange={() => {}} />);
    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).toBeEnabled());
    fireEvent.click(save);
    await waitFor(() => expect(showErrorToast).toHaveBeenCalled());
  });
});
