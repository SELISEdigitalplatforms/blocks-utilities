import { describe, expect, it } from "vitest";
import { editDomainFormSchema } from "./utils";

describe("editDomainFormSchema", () => {
  it("accepts domains with valid subdomains", () => {
    const result = editDomainFormSchema.safeParse({
      domains: [
        {
          itemId: "id-1",
          repoUrl: "https://github.com/x",
          customDeploymentUrl: "https://app",
        },
      ],
    });
    expect(result.success).toBe(true);
  });

  it("rejects an empty itemId", () => {
    const result = editDomainFormSchema.safeParse({
      domains: [{ itemId: "", customDeploymentUrl: "https://app" }],
    });
    expect(result.success).toBe(false);
  });

  it("rejects an empty deployment url", () => {
    const result = editDomainFormSchema.safeParse({
      domains: [{ itemId: "id-1", customDeploymentUrl: "" }],
    });
    expect(result.success).toBe(false);
  });

  it("rejects an invalid subdomain", () => {
    const result = editDomainFormSchema.safeParse({
      domains: [{ itemId: "id-1", customDeploymentUrl: "no-scheme" }],
    });
    expect(result.success).toBe(false);
  });
});
