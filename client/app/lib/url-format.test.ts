import { describe, expect, it } from "vitest";
import { z } from "zod";
import { isParsableUrl } from "./url-format";

describe("isParsableUrl", () => {
  it("accepts absolute URLs of any scheme", () => {
    expect(isParsableUrl("https://api.example.com/v1")).toBe(true);
    expect(isParsableUrl("http://localhost:8080")).toBe(true);
    expect(isParsableUrl("mailto:someone@example.com")).toBe(true);
  });

  it("rejects values the URL parser rejects", () => {
    expect(isParsableUrl("")).toBe(false);
    expect(isParsableUrl("example.com")).toBe(false);
    expect(isParsableUrl("not a url")).toBe(false);
  });

  it("leaves the parsed value untouched in a schema, unlike z.url()", () => {
    const schema = z.string().refine(isParsableUrl, "Enter a valid URL");
    expect(schema.parse(" https://example.com\t")).toBe(" https://example.com\t");
    expect(schema.safeParse("nope").error?.issues[0]?.message).toBe("Enter a valid URL");
  });
});
