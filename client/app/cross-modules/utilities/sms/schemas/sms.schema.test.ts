import { describe, expect, it } from "vitest";
import { SmsProviderType, SmsUrlPolicy } from "../constants/sms.constants";
import { createSmsProviderSchema, sendSmsSchema, smsTemplateSchema, type SmsProviderFormValues } from "./sms.schema";

const twilio = (overrides: Partial<SmsProviderFormValues> = {}): SmsProviderFormValues => ({
  name: "Default",
  providerType: SmsProviderType.Twilio,
  isEnabled: true,
  isDefault: true,
  senderNumber: "+15005550006",
  senderName: "",
  senderNameExcludedPrefixes: ["+1"],
  accountId: `AC${"a".repeat(32)}`,
  apiKey: "token",
  messagingProfileId: "",
  webhookPublicKey: "",
  statusCallbackBaseUrl: "",
  maxRetryAttempts: 5,
  deliveryCheckDelayMinutes: 10,
  rateLimit: { tenantMaxPerWindow: 300, tenantWindowSeconds: 60, recipientMaxPerWindow: 5, recipientWindowSeconds: 300 },
  spamFilter: { enabled: true, maxRecipients: 100, maxMessageLength: 1000, urlPolicy: SmsUrlPolicy.Flag, blockedTerms: [] },
  ...overrides,
});

const errorPaths = (result: ReturnType<ReturnType<typeof createSmsProviderSchema>["safeParse"]>) =>
  result.success ? [] : result.error.issues.map((issue) => issue.path.join("."));

describe("createSmsProviderSchema", () => {
  it("accepts a valid Twilio configuration", () => {
    expect(createSmsProviderSchema(true).safeParse(twilio()).success).toBe(true);
  });

  it("needs a sender number or a sender name", () => {
    expect(errorPaths(createSmsProviderSchema(true).safeParse(twilio({ senderNumber: "" })))).toContain("senderNumber");
    expect(createSmsProviderSchema(true).safeParse(twilio({ senderNumber: "", senderName: "ACME" })).success).toBe(true);
  });

  it.each(["123456", "TwelveChars1", "ACME-Bank"])("rejects the sender name %s", (senderName) => {
    expect(errorPaths(createSmsProviderSchema(true).safeParse(twilio({ senderName })))).toContain("senderName");
  });

  it("requires the key only when none is stored", () => {
    expect(errorPaths(createSmsProviderSchema(true).safeParse(twilio({ apiKey: "" })))).toContain("apiKey");
    expect(createSmsProviderSchema(false).safeParse(twilio({ apiKey: "" })).success).toBe(true);
  });

  it("checks the Twilio account SID", () => {
    expect(errorPaths(createSmsProviderSchema(true).safeParse(twilio({ accountId: "AC123" })))).toContain("accountId");
  });

  it("asks Telnyx for a profile GUID and a webhook key instead", () => {
    const paths = errorPaths(
      createSmsProviderSchema(true).safeParse(twilio({ providerType: SmsProviderType.Telnyx, accountId: "" })),
    );

    expect(paths).toEqual(expect.arrayContaining(["messagingProfileId", "webhookPublicKey"]));
    expect(paths).not.toContain("accountId");
  });

  it("rejects country codes that are not +digits", () => {
    expect(
      errorPaths(createSmsProviderSchema(true).safeParse(twilio({ senderNameExcludedPrefixes: ["1"] }))),
    ).toContain("senderNameExcludedPrefixes.0");
  });

  it("wants an https callback base", () => {
    expect(
      errorPaths(createSmsProviderSchema(true).safeParse(twilio({ statusCallbackBaseUrl: "http://example.com" }))),
    ).toContain("statusCallbackBaseUrl");
  });
});

describe("smsTemplateSchema", () => {
  it("mirrors the server's name and language rules", () => {
    expect(smsTemplateSchema.safeParse({ name: "otp-login", language: "en-US", body: "{{code}}" }).success).toBe(true);
    expect(smsTemplateSchema.safeParse({ name: "otp login", language: "en-US", body: "x" }).success).toBe(false);
    expect(smsTemplateSchema.safeParse({ name: "otp", language: "english", body: "x" }).success).toBe(false);
  });
});

describe("sendSmsSchema", () => {
  const base = {
    mode: "text" as const,
    destinationNumbers: ["+41790000000"],
    messageText: "hi",
    templateName: "",
    language: "",
    dataContext: {},
    correlationId: "",
  };

  it("needs text in text mode and a template in template mode", () => {
    expect(sendSmsSchema.safeParse(base).success).toBe(true);
    expect(sendSmsSchema.safeParse({ ...base, messageText: " " }).success).toBe(false);
    expect(sendSmsSchema.safeParse({ ...base, mode: "template" }).success).toBe(false);
  });

  it("needs at least one well-formed recipient", () => {
    expect(sendSmsSchema.safeParse({ ...base, destinationNumbers: [] }).success).toBe(false);
    expect(sendSmsSchema.safeParse({ ...base, destinationNumbers: ["abc"] }).success).toBe(false);
  });
});
