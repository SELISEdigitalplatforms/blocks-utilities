import { afterEach, describe, expect, it, vi } from "vitest";
import {
  isValidDomain,
  isValidSubdomain,
  getDomain,
  getSubdomain,
  getProjectBlocksApiUrl,
} from "./domain";

describe("isValidDomain", () => {
  it("accepts well-formed http(s) domains", () => {
    expect(isValidDomain("https://example.com")).toBe(true);
    expect(isValidDomain("http://sub.example.co.uk")).toBe(true);
  });

  it("rejects malformed values", () => {
    expect(isValidDomain("example.com")).toBe(false);
    expect(isValidDomain("https://-bad.com")).toBe(false);
    expect(isValidDomain("")).toBe(false);
  });

  it("trims before validating", () => {
    expect(isValidDomain("  https://example.com  ")).toBe(true);
  });
});

describe("isValidSubdomain", () => {
  it("returns false for empty input", () => {
    expect(isValidSubdomain("")).toBe(false);
  });

  it("validates every dot-separated label", () => {
    expect(isValidSubdomain("https://foo")).toBe(true);
  });
});

describe("getDomain", () => {
  it("returns the registrable domain", () => {
    expect(getDomain("https://sub.example.com")).toBe("example.com");
  });

  it("returns empty for invalid domains", () => {
    expect(getDomain("not-a-url")).toBe("");
    expect(getDomain()).toBe("");
  });
});

describe("getSubdomain", () => {
  it("returns empty for empty or invalid input", () => {
    expect(getSubdomain("")).toBe("");
    expect(getSubdomain("bad")).toBe("");
  });

  it("returns empty when there is no subdomain", () => {
    expect(getSubdomain("https://example.com")).toBe("");
  });

  it("returns the protocol-prefixed subdomain when present", () => {
    expect(getSubdomain("https://app.example.com")).toBe("https://app");
  });
});

describe("getProjectBlocksApiUrl", () => {
  const original = import.meta.env.VITE_PROJECT_DEFAULT_API_BASE_URL;

  afterEach(() => {
    vi.unstubAllEnvs();
    import.meta.env.VITE_PROJECT_DEFAULT_API_BASE_URL = original;
  });

  it("returns empty when no project is provided", () => {
    expect(getProjectBlocksApiUrl()).toBe("");
  });

  it("returns empty when the base url env is missing", () => {
    vi.stubEnv("VITE_PROJECT_DEFAULT_API_BASE_URL", "");
    expect(getProjectBlocksApiUrl({ customDomain: "" } as never)).toBe("");
  });

  it("returns the default base when there is no custom domain", () => {
    vi.stubEnv("VITE_PROJECT_DEFAULT_API_BASE_URL", "https://base.api");
    expect(getProjectBlocksApiUrl({ customDomain: "" } as never)).toBe(
      "https://base.api",
    );
  });

  it("derives a blocksapi host from a custom domain", () => {
    vi.stubEnv("VITE_PROJECT_DEFAULT_API_BASE_URL", "https://base.api");
    expect(
      getProjectBlocksApiUrl({
        customDomain: "https://app.acme.com",
      } as never),
    ).toBe("blocksapi.acme.com");
  });
});
