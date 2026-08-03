import { describe, expect, it } from "vitest";
import {
  createClientSchema,
  CreateClientModalFormDefaultValues,
} from "./utils";

describe("create client credential schema", () => {
  it("has sensible defaults", () => {
    expect(CreateClientModalFormDefaultValues.roles).toEqual([]);
  });

  it("accepts a valid client", () => {
    const result = createClientSchema.safeParse({
      clientNameService: "svc",
      audienceUrlService: "https://api.example.com",
      roles: ["admin"],
    });
    expect(result.success).toBe(true);
  });

  it("requires a client name", () => {
    const result = createClientSchema.safeParse({
      clientNameService: "",
      audienceUrlService: "https://api.example.com",
      roles: [],
    });
    expect(result.success).toBe(false);
  });

  it("requires a valid audience url", () => {
    const result = createClientSchema.safeParse({
      clientNameService: "svc",
      audienceUrlService: "not-a-url",
      roles: [],
    });
    expect(result.success).toBe(false);
  });
});
