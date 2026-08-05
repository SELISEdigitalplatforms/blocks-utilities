import { afterEach, describe, expect, it, vi } from "vitest";
import { getDefaultShortUrlBase, isValidUrl, magicUrlSchema } from "./url.util";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { SHORT_URL_BASES } from "@blocks-utilities/magic-url/constants/endpoint.constant";

vi.mock("@/lib/runtime-env", () => ({
  getRuntimeEnv: vi.fn(),
}));

describe("getDefaultShortUrlBase", () => {
  afterEach(() => vi.clearAllMocks());

  it("returns the dev base when the api base contains dev", () => {
    vi.mocked(getRuntimeEnv).mockReturnValue(
      "https://dev-utilities.blocksdevelopers.com",
    );
    expect(getDefaultShortUrlBase()).toBe(SHORT_URL_BASES.dev);
  });

  it("returns the stg base when the api base contains stg", () => {
    vi.mocked(getRuntimeEnv).mockReturnValue(
      "https://stg-utilities.seliseblocks.com",
    );
    expect(getDefaultShortUrlBase()).toBe(SHORT_URL_BASES.stg);
  });

  it("falls back to prod otherwise", () => {
    vi.mocked(getRuntimeEnv).mockReturnValue("https://utilities.seliseblocks.com");
    expect(getDefaultShortUrlBase()).toBe(SHORT_URL_BASES.prod);
  });

  it("falls back to prod when env is empty", () => {
    vi.mocked(getRuntimeEnv).mockReturnValue("");
    expect(getDefaultShortUrlBase()).toBe(SHORT_URL_BASES.prod);
  });
});

describe("isValidUrl", () => {
  it("accepts http and https urls", () => {
    expect(isValidUrl("https://example.com")).toBe(true);
    expect(isValidUrl("http://example.com/path")).toBe(true);
  });

  it("rejects other protocols and garbage", () => {
    expect(isValidUrl("ftp://example.com")).toBe(false);
    expect(isValidUrl("not a url")).toBe(false);
  });
});

describe("magicUrlSchema", () => {
  it("accepts a valid uri and name", () => {
    const result = magicUrlSchema.safeParse({
      uri: "https://example.com",
      name: "My link",
    });
    expect(result.success).toBe(true);
  });

  it("accepts a bare domain uri", () => {
    const result = magicUrlSchema.safeParse({
      uri: "example.com",
      name: "n",
    });
    expect(result.success).toBe(true);
  });

  it("accepts a uri with a path, a trailing slash and a query-free tail", () => {
    for (const uri of [
      "https://example.com/a/b-c",
      "https://example.com/a/b/",
      "http://sub.example.co.uk/deep/path.html",
      "example.com/",
    ]) {
      expect(magicUrlSchema.safeParse({ uri, name: "n" }).success).toBe(true);
    }
  });

  it("rejects uris without a dotted host", () => {
    for (const uri of ["https://", "localhost", "http://localhost"]) {
      expect(magicUrlSchema.safeParse({ uri, name: "n" }).success).toBe(false);
    }
  });

  it("rejects an empty uri", () => {
    const result = magicUrlSchema.safeParse({ uri: "", name: "n" });
    expect(result.success).toBe(false);
  });

  it("rejects an empty name", () => {
    const result = magicUrlSchema.safeParse({
      uri: "https://example.com",
      name: "",
    });
    expect(result.success).toBe(false);
  });

  it("rejects a name over 100 characters", () => {
    const result = magicUrlSchema.safeParse({
      uri: "https://example.com",
      name: "a".repeat(101),
    });
    expect(result.success).toBe(false);
  });
});
