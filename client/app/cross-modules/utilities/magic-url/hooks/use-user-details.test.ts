import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { userService } from "@blocks-idp/iam/services/user.service";
import { useAuthStore } from "@seliseblocks/genesis-os";
import { useGetCreator } from "./use-user-details";

vi.mock("@blocks-idp/iam/services/user.service", () => ({
  userService: { getUser: vi.fn(), getUserById: vi.fn() },
}));

vi.mock("@seliseblocks/genesis-os", () => ({
  useAuthStore: vi.fn(),
}));

describe("useGetCreator", () => {
  beforeEach(() => vi.clearAllMocks());

  it("fetches the current user when createdBy matches the signed-in user", async () => {
    vi.mocked(useAuthStore).mockReturnValue({ user: { sub: "me" } } as never);
    vi.mocked(userService.getUser).mockResolvedValue({
      data: { itemId: "me" },
    } as never);
    const { result } = renderHook(() => useGetCreator("me", "tenant"), {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(userService.getUser).toHaveBeenCalled();
    expect(userService.getUserById).not.toHaveBeenCalled();
  });

  it("fetches by id when createdBy is another user", async () => {
    vi.mocked(useAuthStore).mockReturnValue({ user: { sub: "me" } } as never);
    vi.mocked(userService.getUserById).mockResolvedValue({
      data: { itemId: "other" },
    } as never);
    const { result } = renderHook(() => useGetCreator("other", "tenant"), {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(userService.getUserById).toHaveBeenCalledWith({
      id: "other",
      projectKey: "tenant",
    });
  });

  it("is disabled when createdBy is empty", () => {
    vi.mocked(useAuthStore).mockReturnValue({ user: { sub: "me" } } as never);
    const { result } = renderHook(() => useGetCreator(null, "tenant"), {
      wrapper: createWrapper(),
    });
    expect(result.current.fetchStatus).toBe("idle");
  });
});
