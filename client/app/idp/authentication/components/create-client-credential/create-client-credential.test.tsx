import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { CreateClientCredential } from "./create-client-credential";

const saveServiceClient = vi.fn();
let isPending = false;
vi.mock("@blocks-idp/authentication/hooks/use-auth-clients", () => ({
  useSaveAuthClient: () => ({ mutateAsync: saveServiceClient, isPending }),
}));

let rolesResult: { data?: { data: { slug: string }[] }; isLoading: boolean } = {
  data: { data: [{ slug: "admin" }, { slug: "viewer" }] },
  isLoading: false,
};
vi.mock("@blocks-idp/iam/hooks/use-roles", () => ({
  useGetRoles: () => rolesResult,
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const openDialog = async () => {
  const user = userEvent.setup();
  render(<CreateClientCredential />);
  await user.click(screen.getAllByRole("button", { name: /Create/ })[0]);
  await screen.findByText("New Access Token");
  return user;
};

describe("CreateClientCredential", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isPending = false;
    rolesResult = {
      data: { data: [{ slug: "admin" }, { slug: "viewer" }] },
      isLoading: false,
    };
    useProjectStore.setState({ selectedProject: { tenantId: "tg1" } });
  });

  it("opens the dialog and lists roles", async () => {
    await openDialog();
    expect(screen.getByLabelText("Client Name")).toBeInTheDocument();
    expect(screen.getByText("admin")).toBeInTheDocument();
    expect(screen.getByText("viewer")).toBeInTheDocument();
  });

  it("shows an empty roles message when there are none", async () => {
    rolesResult = { data: { data: [] }, isLoading: false };
    await openDialog();
    expect(screen.getByText("No roles found")).toBeInTheDocument();
  });

  it("submits and reports success", async () => {
    saveServiceClient.mockResolvedValue({ isSuccess: true });
    await openDialog();
    fireEvent.change(screen.getByLabelText("Client Name"), {
      target: { value: "svc-1" },
    });
    fireEvent.change(screen.getByLabelText("Audience"), {
      target: { value: "https://aud.example.com" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Add" }));
    await waitFor(() => expect(saveServiceClient).toHaveBeenCalled());
    expect(saveServiceClient).toHaveBeenCalledWith(
      expect.objectContaining({ name: "svc-1", projectKey: "tg1" }),
    );
    expect(showSuccessToast).toHaveBeenCalled();
  });

  it("shows an error toast when the save is unsuccessful", async () => {
    saveServiceClient.mockResolvedValue({ isSuccess: false, error: "boom" });
    await openDialog();
    fireEvent.change(screen.getByLabelText("Client Name"), {
      target: { value: "svc-1" },
    });
    fireEvent.change(screen.getByLabelText("Audience"), {
      target: { value: "https://aud.example.com" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Add" }));
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "boom" }),
    );
  });
});
