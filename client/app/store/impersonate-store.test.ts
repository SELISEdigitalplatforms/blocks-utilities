import { beforeEach, describe, expect, it } from "vitest";
import { useImpersonateStore } from "./impersonate-store";

describe("useImpersonateStore", () => {
  beforeEach(() => useImpersonateStore.getState().reset());

  it("setImpersonation sets all three fields", () => {
    useImpersonateStore.getState().setImpersonation(true, "orig", "imp");
    const s = useImpersonateStore.getState();
    expect(s.isImpersonated).toBe(true);
    expect(s.originalTenantId).toBe("orig");
    expect(s.impersonatedTenantId).toBe("imp");
  });

  it("impersonate marks impersonation active", () => {
    useImpersonateStore.getState().impersonate("imp", "orig");
    const s = useImpersonateStore.getState();
    expect(s.isImpersonated).toBe(true);
    expect(s.impersonatedTenantId).toBe("imp");
    expect(s.originalTenantId).toBe("orig");
  });

  it("terminate clears the impersonated tenant but keeps the original", () => {
    useImpersonateStore.getState().impersonate("imp", "orig");
    useImpersonateStore.getState().terminate("orig");
    const s = useImpersonateStore.getState();
    expect(s.isImpersonated).toBe(false);
    expect(s.impersonatedTenantId).toBeNull();
    expect(s.originalTenantId).toBe("orig");
  });

  it("setInitialized toggles the flag", () => {
    useImpersonateStore.getState().setInitialized(true);
    expect(useImpersonateStore.getState().isInitialized).toBe(true);
  });

  it("reset restores defaults", () => {
    useImpersonateStore.getState().impersonate("imp", "orig");
    useImpersonateStore.getState().setInitialized(true);
    useImpersonateStore.getState().reset();
    const s = useImpersonateStore.getState();
    expect(s.isImpersonated).toBe(false);
    expect(s.impersonatedTenantId).toBeNull();
    expect(s.originalTenantId).toBeNull();
    expect(s.isInitialized).toBe(false);
  });
});
