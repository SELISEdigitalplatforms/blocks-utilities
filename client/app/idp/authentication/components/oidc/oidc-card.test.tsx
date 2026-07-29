import { describe, it, expect, vi, beforeEach } from "vitest";
import type { ReactNode } from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { OIDCCard } from "./oidc-card";

const deleteOidc = vi.fn();
let isPending = false;
vi.mock("@blocks-idp/authentication/hooks/use-auth-oidc", () => ({
  useDeleteAuthOidc: () => ({ mutateAsync: deleteOidc, isPending }),
}));

// CreateOIDC pulls in storage/upload hooks that are not relevant here, so it is
// replaced with a marker that keeps the trash button the only real button.
vi.mock("../create-oidc/create-oidc", () => ({
  CreateOIDC: () => <div data-testid="create-oidc" />,
}));

// CopyToClipboardButton renders its own buttons; stub it to plain content so
// the trash button is unambiguous while still rendering the copied values.
vi.mock("@/components/copy-to-clipboard-button", () => ({
  CopyToClipboardButton: ({ children }: { children: ReactNode }) => (
    <div>{children}</div>
  ),
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const oidc = {
  itemId: "oidc-123456789",
  clientDisplayName: "Acme OIDC",
  clientSecret: "secret-value-1234",
  redirectUri: "https://acme.test/callback",
  audience: "https://acme.test",
  scope: "openid profile",
  createdDate: "2024-01-02T10:30:00Z",
  clientBrandColor: "#abc123",
  clientLogoUrl: "https://acme.test/logo.png",
} as never;

describe("OIDCCard", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isPending = false;
    useProjectStore.setState({ selectedProject: { tenantId: "tg-1" } });
  });

  it("renders the client details and scope badge", () => {
    render(<OIDCCard oidc={oidc} />);
    expect(screen.getByText("Acme OIDC")).toBeInTheDocument();
    expect(screen.getByText("openid profile")).toBeInTheDocument();
    expect(screen.getByText("Client Id")).toBeInTheDocument();
    expect(screen.getByText("Redirect URL")).toBeInTheDocument();
    expect(screen.getByAltText("OIDC Logo")).toBeInTheDocument();
  });

  it("shows N/A for the scope when none is set", () => {
    render(<OIDCCard oidc={{ ...oidc, scope: "" }} />);
    expect(screen.getAllByText("N/A").length).toBeGreaterThan(0);
  });

  it("deletes the credential through the confirmation dialog", async () => {
    deleteOidc.mockResolvedValue({ isSuccess: true });
    const user = userEvent.setup();
    render(<OIDCCard oidc={oidc} />);

    await user.click(screen.getByRole("button"));
    const confirm = await screen.findByRole("button", { name: "Yes" });
    await user.click(confirm);

    await waitFor(() =>
      expect(deleteOidc).toHaveBeenCalledWith({
        itemId: "oidc-123456789",
        projectKey: "tg-1",
      }),
    );
    expect(showSuccessToast).toHaveBeenCalled();
  });

  it("reports an error toast when the delete is unsuccessful", async () => {
    deleteOidc.mockResolvedValue({ isSuccess: false, error: "nope" });
    const user = userEvent.setup();
    render(<OIDCCard oidc={oidc} />);

    await user.click(screen.getByRole("button"));
    await user.click(await screen.findByRole("button", { name: "Yes" }));

    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "nope" }),
    );
    expect(showSuccessToast).not.toHaveBeenCalled();
  });
});
