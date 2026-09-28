import { HttpError } from "@seliseblocks/genesis-os/lib";
import { describe, expect, it, vi } from "vitest";

const http = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }));

vi.mock("@/lib/http-client", () => ({ serviceInstances: { utitlitiesService: http } }));

import {
  SubscriptionOperationError,
  subscriptionSimulationService,
} from "./subscription-simulation.service";

describe("a refused quantity change", () => {
  /**
   * Found testing in the portal: an HttpError carries the whole response envelope as `errors`, so
   * the search for a string among its values found none and the dialog showed the raw JSON.
   */
  it("carries the server's own sentence and code, not the response body", async () => {
    http.put.mockRejectedValue(
      new HttpError(409, {
        errors: {
          success: false,
          data: null,
          error: {
            code: "subscription_member_seats_occupied",
            message: "2 people have to come off before this change can take their seats away.",
          },
        } as unknown as Record<string, string>,
      }),
    );

    const failure = await subscriptionSimulationService
      .changeQuantity("sub-1", { version: 1, quantities: [] })
      .catch((error: unknown) => error);

    expect(failure).toBeInstanceOf(SubscriptionOperationError);
    expect((failure as SubscriptionOperationError).code).toBe("subscription_member_seats_occupied");
    expect((failure as SubscriptionOperationError).message).toBe(
      "2 people have to come off before this change can take their seats away.",
    );
  });
});
