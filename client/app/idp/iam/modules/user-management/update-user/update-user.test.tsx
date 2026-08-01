import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { UpdateUser } from "./update-user";

const h = vi.hoisted(() => ({
  meResponse: {
    data: { firstName: "Ada", lastName: "Lovelace", itemId: "u1" } as Record<string, unknown>,
    isLoading: false,
    isFetching: false,
  },
  updateUser: vi.fn(),
  isPending: false,
}));
const updateUser = h.updateUser;
vi.mock("@/idp/iam/hooks/use-user", () => ({
  useGetMe: () => h.meResponse,
  useUpdateUser: () => ({ isPending: h.isPending, mutateAsync: h.updateUser }),
}));

const showSuccessToast = vi.fn();
const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: (...a: unknown[]) => showSuccessToast(...a),
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const openDialog = async () => {
  fireEvent.click(screen.getByRole("button", { name: /Edit User/i }));
  await waitFor(() =>
    expect(screen.getByPlaceholderText("Enter first name")).toBeInTheDocument(),
  );
};

const editNames = async (user: ReturnType<typeof userEvent.setup>) => {
  const first = screen.getByPlaceholderText("Enter first name");
  const last = screen.getByPlaceholderText("Enter last name");
  await user.clear(first);
  await user.type(first, "Grace");
  await user.clear(last);
  await user.type(last, "Hopper");
};

describe("UpdateUser", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    h.isPending = false;
    h.meResponse.data = { firstName: "Ada", lastName: "Lovelace", itemId: "u1" };
  });

  it("opens the dialog with the name fields and a disabled Save button", async () => {
    render(<UpdateUser id="u1" projectKey="p1" />);
    await openDialog();

    expect(screen.getByText("First name")).toBeInTheDocument();
    expect(screen.getByText("Last name")).toBeInTheDocument();
    // Save is disabled until the form becomes dirty.
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
  });

  it("enables Save after editing and submits an update with a success toast", async () => {
    updateUser.mockResolvedValue({ isSuccess: true });
    const user = userEvent.setup();
    render(<UpdateUser id="u1" projectKey="p1" own />);
    await openDialog();
    await editNames(user);

    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).toBeEnabled());
    await user.click(save);

    await waitFor(() =>
      expect(updateUser).toHaveBeenCalledWith(
        expect.objectContaining({
          firstName: "Grace",
          lastName: "Hopper",
          itemId: "u1",
          projectKey: "p1",
        }),
      ),
    );
    expect(showSuccessToast).toHaveBeenCalled();
  });

  it("shows an error toast when the update is unsuccessful", async () => {
    updateUser.mockResolvedValue({ isSuccess: false, errors: "bad" });
    const user = userEvent.setup();
    render(<UpdateUser id="u1" projectKey="p1" />);
    await openDialog();
    await editNames(user);
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(showErrorToast).toHaveBeenCalledWith({ errors: "bad" }));
    expect(showSuccessToast).not.toHaveBeenCalled();
  });

  it("shows a generic error toast when the mutation throws", async () => {
    updateUser.mockRejectedValue(new Error("network"));
    const user = userEvent.setup();
    render(<UpdateUser id="u1" projectKey="p1" />);
    await openDialog();
    await editNames(user);
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "Something went wrong" }),
    );
  });
});
