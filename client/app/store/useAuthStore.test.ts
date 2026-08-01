import { beforeEach, describe, expect, it } from "vitest";
import { useAuthStore } from "./useAuthStore";

describe("useAuthStore", () => {
  beforeEach(() => {
    useAuthStore.getState().reset();
  });

  it("setUser sets the user and authentication flag", () => {
    useAuthStore.getState().setUser({ itemId: "u" } as never);
    expect(useAuthStore.getState().user).toEqual({ itemId: "u" });
    expect(useAuthStore.getState().isAuthenticated).toBe(true);
  });

  it("setUser with null deauthenticates", () => {
    useAuthStore.getState().setUser(null);
    expect(useAuthStore.getState().isAuthenticated).toBe(false);
  });

  it("setAuthenticated and setUnAuthenticated toggle state", () => {
    useAuthStore.getState().setAuthenticated();
    expect(useAuthStore.getState().isAuthenticated).toBe(true);
    useAuthStore.getState().setUser({ itemId: "u" } as never);
    useAuthStore.getState().setUnAuthenticated();
    expect(useAuthStore.getState().isAuthenticated).toBe(false);
    expect(useAuthStore.getState().user).toBeNull();
  });

  it("setTokens and clearTokens manage tokens", () => {
    useAuthStore.getState().setTokens("access", "refresh");
    expect(useAuthStore.getState().accessToken).toBe("access");
    expect(useAuthStore.getState().refreshToken).toBe("refresh");
    useAuthStore.getState().clearTokens();
    expect(useAuthStore.getState().accessToken).toBeNull();
    expect(useAuthStore.getState().refreshToken).toBeNull();
  });

  it("reset restores the default state", () => {
    useAuthStore.getState().setUser({ itemId: "u" } as never);
    useAuthStore.getState().setTokens("a", "b");
    useAuthStore.getState().reset();
    const s = useAuthStore.getState();
    expect(s.isAuthenticated).toBe(false);
    expect(s.user).toBeNull();
    expect(s.accessToken).toBeNull();
    expect(s.refreshToken).toBeNull();
  });
});
