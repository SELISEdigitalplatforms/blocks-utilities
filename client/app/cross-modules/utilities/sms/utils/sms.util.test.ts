import { describe, expect, it, vi } from "vitest";
import { applyServerErrors, countSegments, extractPlaceholders, toFormPath } from "./sms.util";

describe("toFormPath", () => {
  it("camel-cases every segment of a FluentValidation property path", () => {
    expect(toFormPath("SenderNumber")).toBe("senderNumber");
    expect(toFormPath("RateLimit.TenantMaxPerWindow")).toBe("rateLimit.tenantMaxPerWindow");
  });
});

describe("applyServerErrors", () => {
  it("places known fields and returns the rest for a banner", () => {
    const setError = vi.fn();

    const unplaced = applyServerErrors(
      { SenderName: "Too long.", Tenant: "The request has no tenant context." },
      ["senderName"],
      setError,
    );

    expect(setError).toHaveBeenCalledWith("senderName", { type: "server", message: "Too long." });
    expect(unplaced).toEqual(["The request has no tenant context."]);
  });
});

describe("extractPlaceholders", () => {
  it("lists each key once, tolerating spaces inside the braces", () => {
    expect(extractPlaceholders("Hi {{ name }}, {{code}} — {{Name}} again")).toEqual(["name", "code"]);
  });
});

describe("countSegments", () => {
  it("fits 160 GSM-7 characters in one segment and splits at 153", () => {
    expect(countSegments("a".repeat(160))).toEqual({ encoding: "GSM-7", segments: 1 });
    expect(countSegments("a".repeat(161))).toEqual({ encoding: "GSM-7", segments: 2 });
  });

  it("counts extension characters twice", () => {
    expect(countSegments("€".repeat(80)).segments).toBe(1);
    expect(countSegments("€".repeat(81)).segments).toBe(2);
  });

  it("drops to UCS-2 for anything outside GSM-7", () => {
    expect(countSegments("😀".repeat(70))).toEqual({ encoding: "UCS-2", segments: 1 });
    expect(countSegments("😀".repeat(71))).toEqual({ encoding: "UCS-2", segments: 2 });
  });

  it("is zero for an empty body", () => {
    expect(countSegments("").segments).toBe(0);
  });
});
