import { beforeEach, describe, expect, it, vi } from "vitest";

const { getRuntimeEnv } = vi.hoisted(() => ({
  getRuntimeEnv: vi.fn(),
}));

vi.mock("@/lib/runtime-env", () => ({ getRuntimeEnv }));

import { webhookEndpointsFor } from "./webhook-endpoint.util";

describe("webhookEndpointsFor", () => {
  beforeEach(() => {
    getRuntimeEnv.mockReturnValue("https://utilities.example.com");
  });

  /**
   * These paths are copied by hand into a provider's dashboard, so a wrong one is not a broken
   * page — it is a provider posting events into the void and payments that never leave
   * Processing. Pinned literally against Api/Controllers/PaymentWebhooksController.cs, which
   * mounts them without the /api prefix the rest of the service uses.
   */
  it("uses the paths the server actually listens on", () => {
    expect(webhookEndpointsFor("STRIPE")[0].url).toBe(
      "https://utilities.example.com/payments/stripe/webhooks",
    );
    expect(webhookEndpointsFor("ADYEN-ONLINE").map((e) => e.url)).toEqual([
      "https://utilities.example.com/payments/adyen/webhooks/standard",
      "https://utilities.example.com/payments/adyen/webhooks/tokens",
    ]);
  });

  /** No /api segment: the webhook controller skips the global prefix. */
  it("does not prefix the webhook paths with api", () => {
    webhookEndpointsFor("ADYEN-ONLINE").forEach((endpoint) =>
      expect(endpoint.url).not.toContain("/api/"),
    );
  });

  /**
   * Adyen raises stored-payment-method events on a separate notification configuration, so a
   * merchant who registers only the standard one never gets saved cards.
   */
  it("gives Adyen both endpoints and Stripe one", () => {
    expect(webhookEndpointsFor("ADYEN-ONLINE")).toHaveLength(2);
    expect(webhookEndpointsFor("STRIPE")).toHaveLength(1);
  });

  it("does not double the slash when the configured base has a trailing one", () => {
    getRuntimeEnv.mockReturnValue("https://utilities.example.com/");

    expect(webhookEndpointsFor("STRIPE")[0].url).toBe(
      "https://utilities.example.com/payments/stripe/webhooks",
    );
  });

  /**
   * In production the API serves the console from its own wwwroot, so its origin is the origin
   * the webhooks arrive at. Better than showing a path with nothing in front of it.
   */
  it("falls back to the page origin when the setting is unset", () => {
    getRuntimeEnv.mockReturnValue("");

    expect(webhookEndpointsFor("STRIPE")[0].url).toBe(
      `${window.location.origin}/payments/stripe/webhooks`,
    );
  });
});
