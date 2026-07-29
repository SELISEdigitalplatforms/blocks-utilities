import { renderHook, waitFor, act } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";

const h = vi.hoisted(() => ({
  service: {
    getUserInfo: vi.fn(),
    me: vi.fn(),
    getUserById: vi.fn(),
    updateUser: vi.fn(),
  },
  setUser: vi.fn(),
}));

vi.mock("@blocks-idp/iam/services/user.service", () => ({
  userService: h.service,
}));
vi.mock("@seliseblocks/genesis-os", () => ({
  useAuthStore: () => ({ setUser: h.setUser }),
}));

import {
  useGetUserInfo,
  useGetMe,
  useUserRoles,
  useUserPermissions,
} from "./use-user";

describe("use-user additional hooks", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("useGetUserInfo fetches the current user info", async () => {
    h.service.getUserInfo.mockResolvedValue({ itemId: "u1" });
    const { result } = renderHook(() => useGetUserInfo(), { wrapper: createWrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual({ itemId: "u1" });
  });

  it("useGetMe fetches the current user and populates the auth store", async () => {
    const user = { itemId: "me-1", firstName: "Ada" };
    h.service.me.mockResolvedValue({ data: user });
    const { result } = renderHook(() => useGetMe(), { wrapper: createWrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(h.service.me).toHaveBeenCalled();
    expect(h.setUser).toHaveBeenCalledWith(user);
  });

  describe("useUserRoles", () => {
    const option = { id: "u1", projectKey: "p1" };

    beforeEach(() => {
      h.service.getUserById.mockResolvedValue({
        data: { itemId: "u1", firstName: "Ada" },
        roles: [{ slug: "admin" }, { slug: "editor" }],
        permissions: [],
      });
      h.service.updateUser.mockResolvedValue({ isSuccess: true });
    });

    it("exposes the current role slugs", async () => {
      const { result } = renderHook(() => useUserRoles(option), { wrapper: createWrapper() });
      await waitFor(() => expect(result.current.slugs).toEqual(["admin", "editor"]));
      expect(result.current.roles).toHaveLength(2);
    });

    it("adds roles by merging with existing slugs", async () => {
      const { result } = renderHook(() => useUserRoles(option), { wrapper: createWrapper() });
      await waitFor(() => expect(result.current.slugs).toEqual(["admin", "editor"]));

      await act(async () => {
        await result.current.addRoles(["viewer"]);
      });
      expect(h.service.updateUser).toHaveBeenCalledWith(
        expect.objectContaining({
          itemId: "u1",
          projectKey: "p1",
          roles: ["admin", "editor", "viewer"],
        }),
        expect.anything(),
      );
    });

    it("deletes roles by removing the given slugs", async () => {
      const { result } = renderHook(() => useUserRoles(option), { wrapper: createWrapper() });
      await waitFor(() => expect(result.current.slugs).toEqual(["admin", "editor"]));

      await act(async () => {
        await result.current.deleteRoles(["admin"]);
      });
      expect(h.service.updateUser).toHaveBeenCalledWith(
        expect.objectContaining({ roles: ["editor"], itemId: "u1", projectKey: "p1" }),
        expect.anything(),
      );
    });
  });

  describe("useUserPermissions", () => {
    const option = { userId: "u1", projectKey: "p1" };

    beforeEach(() => {
      h.service.getUserById.mockResolvedValue({
        data: { itemId: "u1" },
        roles: [],
        permissions: [{ resource: "read" }, { resource: "write" }],
      });
      h.service.updateUser.mockResolvedValue({ isSuccess: true });
    });

    it("exposes the current resources", async () => {
      const { result } = renderHook(() => useUserPermissions(option), { wrapper: createWrapper() });
      await waitFor(() => expect(result.current.resources).toEqual(["read", "write"]));
    });

    it("adds permissions by merging with existing resources", async () => {
      const { result } = renderHook(() => useUserPermissions(option), { wrapper: createWrapper() });
      await waitFor(() => expect(result.current.resources).toEqual(["read", "write"]));

      await act(async () => {
        await result.current.addPermissions(["delete"]);
      });
      expect(h.service.updateUser).toHaveBeenCalledWith(
        expect.objectContaining({ permissions: ["read", "write", "delete"] }),
        expect.anything(),
      );
    });

    it("deletes permissions by removing the given resources", async () => {
      const { result } = renderHook(() => useUserPermissions(option), { wrapper: createWrapper() });
      await waitFor(() => expect(result.current.resources).toEqual(["read", "write"]));

      await act(async () => {
        await result.current.deletePermissions(["read"]);
      });
      expect(h.service.updateUser).toHaveBeenCalledWith(
        expect.objectContaining({ permissions: ["write"] }),
        expect.anything(),
      );
    });
  });
});
