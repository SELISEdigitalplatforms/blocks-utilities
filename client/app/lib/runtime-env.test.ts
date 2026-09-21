import { afterEach, describe, expect, it, vi } from "vitest";
import { getRuntimeEnv } from "./runtime-env";
import { getApiUrl } from "./get-api-path";

const originalBlocksEnv = window.__BLOCKS_ENV__;

afterEach(() => {
  vi.unstubAllGlobals();
  window.__BLOCKS_ENV__ = originalBlocksEnv;
  vi.unstubAllEnvs();
});

describe("Blocks Utilities API origin", () => {
  it("uses the preview page origin instead of a different runtime or build-time URL", () => {
    window.__BLOCKS_ENV__ = {
      ...originalBlocksEnv,
      BLOCKS_UTILITIES_BASE_URL: "https://shared-utilities.example",
    };
    vi.stubEnv("BLOCKS_UTILITIES_BASE_URL", "https://built-utilities.example");

    expect(getRuntimeEnv("BLOCKS_UTILITIES_BASE_URL")).toBe(
      window.location.origin,
    );
    expect(getApiUrl("utilities", "payments")).toBe(
      `${window.location.origin}/api/payments`,
    );
  });

  it("keeps the configured URL available without a browser", () => {
    vi.stubGlobal("window", undefined);
    vi.stubEnv("BLOCKS_UTILITIES_BASE_URL", "https://external-utilities.example");

    expect(getRuntimeEnv("BLOCKS_UTILITIES_BASE_URL")).toBe(
      "https://external-utilities.example",
    );
  });
});
