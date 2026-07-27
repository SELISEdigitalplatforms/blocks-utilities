import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { ClientCredentialsCard } from "./client-credential-card";

const deleteClient = vi.fn();
let isPending = false;
vi.mock("@blocks-idp/authentication/hooks/use-auth-clients", () => ({
  useDeleteAuthClient: () => ({ mutateAsync: deleteClient, isPending }),
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const client = {
  itemId: "client-123456",
  name: "CI Runner",
  clientSecret: "cc-secret-value",
  audiences: ["https://api.test"],
  roles: ["admin", "reader"],
  isActive: true,
  createdDate: "2024-01-02T10:30:00Z",
} as never;

describe("ClientCredentialsCard", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isPending = false;
    useProjectStore.setState({ selectedProject: { tenantId: "tg-1" } });
  });

  it("renders the name, active badge, audiences and roles", () => {
    render(<ClientCredentialsCard clientCredential={client} />);
    expect(screen.getByText("CI Runner")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
    expect(screen.getByText("https://api.test")).toBeInTheDocument();
    expect(screen.getByText("admin")).toBeInTheDocument();
    expect(screen.getByText("reader")).toBeInTheDocument();
  });

  it("shows N/A when there are no roles or audiences", () => {
    render(
      <ClientCredentialsCard
        clientCredential={{ ...client, roles: [], audiences: [] }}
      />,
    );
    expect(screen.getAllByText("N/A").length).toBe(2);
  });

  it("deletes the credential through the confirmation dialog", async () => {
    deleteClient.mockResolvedValue({ isSuccess: true });
    const user = userEvent.setup();
    render(<ClientCredentialsCard clientCredential={client} />);

    await user.click(screen.getByRole("button", { name: "Delete" }));
    await user.click(await screen.findByRole("button", { name: "Yes" }));

    await waitFor(() =>
      expect(deleteClient).toHaveBeenCalledWith({
        itemId: "client-123456",
        projectKey: "tg-1",
      }),
    );
    expect(showSuccessToast).toHaveBeenCalled();
  });

  it("reports an error toast when the delete fails", async () => {
    deleteClient.mockResolvedValue({ isSuccess: false, error: "denied" });
    const user = userEvent.setup();
    render(<ClientCredentialsCard clientCredential={client} />);

    await user.click(screen.getByRole("button", { name: "Delete" }));
    await user.click(await screen.findByRole("button", { name: "Yes" }));

    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "denied" }),
    );
  });
});
