import { beforeEach, describe, expect, it, vi } from "vitest";
import { getApiPath, getApiUrl } from "./get-api-path";
import { getRuntimeEnv } from "@/lib/runtime-env";

vi.mock("@/lib/runtime-env", () => ({
  getRuntimeEnv: vi.fn(() => "https://base.example"),
}));

describe("getApiPath", () => {
  it("always returns the global /api prefix", () => {
    expect(getApiPath("anything")).toBe("/api");
    expect(getApiPath("")).toBe("/api");
  });
});

describe("getApiUrl", () => {
  beforeEach(() => vi.clearAllMocks());

  it("joins the runtime base, /api and the endpoint", () => {
    expect(getApiUrl("svc", "Authentication/Login")).toBe(
      "https://base.example/api/Authentication/Login",
    );
    expect(getRuntimeEnv).toHaveBeenCalledWith("BLOCKS_UTILITIES_BASE_URL");
  });
});
