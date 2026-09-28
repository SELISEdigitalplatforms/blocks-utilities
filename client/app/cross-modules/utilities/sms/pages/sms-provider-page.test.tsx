import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SmsProviderType, SmsUrlPolicy } from "../constants/sms.constants";
import type { SmsProviderConfiguration } from "../models/sms.model";

const mutateAsync = vi.fn();
let configurationState: { data?: SmsProviderConfiguration | null; isLoading: boolean; isError: boolean; error?: unknown };

vi.mock("@seliseblocks/genesis-os", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "tenant-a" } }),
}));
vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));
vi.mock("../hooks/use-sms", () => ({
  useSmsProviderConfiguration: () => ({ ...configurationState, refetch: vi.fn() }),
  useSaveSmsProviderConfiguration: () => ({ mutateAsync, isPending: false }),
}));

import { SmsRequestError } from "../services/sms.service";
import { SmsProviderPage, toSaveRequest } from "./sms-provider-page";

const stored = (overrides: Partial<SmsProviderConfiguration> = {}): SmsProviderConfiguration => ({
  itemId: "config-1",
  name: "Default",
  providerType: SmsProviderType.Twilio,
  isDefault: true,
  isEnabled: true,
  senderNumber: "+15005550006",
  senderName: "ACME",
  senderNameExcludedPrefixes: ["+1"],
  accountId: `AC${"a".repeat(32)}`,
  hasApiKey: true,
  messagingProfileId: null,
  webhookPublicKey: null,
  statusCallbackBaseUrl: "https://utilities.example.com",
  maxRetryAttempts: 5,
  deliveryCheckDelayMinutes: 10,
  rateLimit: { tenantMaxPerWindow: 300, tenantWindowSeconds: 60, recipientMaxPerWindow: 5, recipientWindowSeconds: 300 },
  spamFilter: { enabled: true, maxRecipients: 100, maxMessageLength: 1000, urlPolicy: SmsUrlPolicy.Flag, blockedTerms: [] },
  lastUpdatedDate: "2026-09-26T10:00:00Z",
  ...overrides,
});

describe("SmsProviderPage", () => {
  beforeEach(() => {
    mutateAsync.mockReset();
    configurationState = { data: stored(), isLoading: false, isError: false };
  });

  it("shows where callbacks go, built the way the server builds it", () => {
    render(<SmsProviderPage />);

    expect(screen.getByText("https://utilities.example.com/sms/twilio/webhooks/tenant-a")).toBeTruthy();
  });

  it("saves without a key when one is stored, keeping the stored one", async () => {
    mutateAsync.mockResolvedValue({ isSuccess: true });
    render(<SmsProviderPage />);

    fireEvent.click(screen.getByRole("button", { name: /save changes/i }));

    await waitFor(() => expect(mutateAsync).toHaveBeenCalled());
    const request = mutateAsync.mock.calls[0][0];
    expect(request.configurationId).toBe("config-1");
    expect(request.apiKey).toBeUndefined();
    expect(request.senderName).toBe("ACME");
  });

  it("asks for the key when none is stored yet", async () => {
    configurationState = { data: null, isLoading: false, isError: false };
    render(<SmsProviderPage />);

    fireEvent.click(screen.getByRole("button", { name: /save provider/i }));

    expect(await screen.findByText("The provider key is required for a new configuration.")).toBeTruthy();
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it("puts server errors under their field and the rest in a banner", async () => {
    mutateAsync.mockRejectedValue(
      new SmsRequestError("x", { SenderName: "Rejected by the server.", Tenant: "The request has no tenant context." }, 400),
    );
    render(<SmsProviderPage />);

    fireEvent.click(screen.getByRole("button", { name: /save changes/i }));

    expect(await screen.findByText("Rejected by the server.")).toBeTruthy();
    expect(screen.getByRole("alert").textContent).toContain("The request has no tenant context.");
  });
});

describe("toSaveRequest", () => {
  it("drops the other provider's identifiers and empty optionals", () => {
    const request = toSaveRequest(
      {
        name: "Default",
        providerType: SmsProviderType.Telnyx,
        isEnabled: true,
        isDefault: true,
        senderNumber: "+15005550006",
        senderName: " ",
        senderNameExcludedPrefixes: [],
        accountId: `AC${"a".repeat(32)}`,
        apiKey: "",
        messagingProfileId: "00000000-0000-0000-0000-000000000001",
        webhookPublicKey: "key",
        statusCallbackBaseUrl: "",
        maxRetryAttempts: 5,
        deliveryCheckDelayMinutes: 10,
        rateLimit: stored().rateLimit,
        spamFilter: stored().spamFilter,
      },
      undefined,
    );

    expect(request.accountId).toBeUndefined();
    expect(request.senderName).toBeUndefined();
    expect(request.apiKey).toBeUndefined();
    expect(request.statusCallbackBaseUrl).toBeUndefined();
    expect(request.messagingProfileId).toBe("00000000-0000-0000-0000-000000000001");
  });
});
