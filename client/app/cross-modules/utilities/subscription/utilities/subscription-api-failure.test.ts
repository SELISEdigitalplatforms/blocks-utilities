import { describe, expect, it } from "vitest";
import {
  billingProfileGapOf,
  subscriptionApiFailure,
} from "./subscription-api-failure";

/** The envelope the API returns, as it reaches the client for a refused call. */
const envelope = (fields: Record<string, string[]>) => ({
  success: false,
  data: null,
  error: {
    code: "subscription_billing_profile_incomplete",
    message: "This organization's billing profile is missing details an invoice must carry.",
    fields,
    traceId: "corr-1",
  },
});

describe("subscription api failure", () => {
  it("reads the envelope out of a failing status", () => {
    // A 400 arrives as an HttpError whose `errors` is the parsed body, so the envelope sits a level
    // down. Read from the wrong level, every named refusal becomes "something went wrong".
    const failure = subscriptionApiFailure({
      status: 400,
      errors: envelope({ BillingProfile: ["LegalName"] }),
    });

    expect(failure?.code).toBe("subscription_billing_profile_incomplete");
    expect(failure?.fields.BillingProfile).toEqual(["LegalName"]);
  });

  it("reads the envelope out of a body that came back with a 200", () => {
    const failure = subscriptionApiFailure(envelope({ BillingProfile: ["LegalName"] }));

    expect(failure?.code).toBe("subscription_billing_profile_incomplete");
  });

  it("is nothing when there is no envelope to read", () => {
    expect(subscriptionApiFailure(new Error("Network request failed"))).toBeNull();
    expect(subscriptionApiFailure(undefined)).toBeNull();
  });

  it("tolerates a field map whose values are plain strings", () => {
    const failure = subscriptionApiFailure({
      error: { code: "subscription_request_invalid", fields: { PriceId: "Required." } },
    });

    expect(failure?.fields.PriceId).toEqual(["Required."]);
  });
});

describe("billing profile gap", () => {
  it("separates what the subscriber owes from what the tenant owes", () => {
    const gap = billingProfileGapOf(
      subscriptionApiFailure(
        envelope({ BillingProfile: ["LegalName", "merchantLegalName"] }),
      ),
    );

    // Two different people fix these on two different screens. Sending a subscriber to add a legal
    // name, when the missing one is the platform's own, sends them to correct something that was
    // never theirs.
    expect(gap).toEqual({ subscriberFields: ["LegalName"], merchantMissing: true });
  });

  it("is nothing for any other refusal", () => {
    expect(
      billingProfileGapOf({ code: "subscription_discount_unknown", message: "", fields: {} }),
    ).toBeNull();
    expect(billingProfileGapOf(null)).toBeNull();
  });
});
