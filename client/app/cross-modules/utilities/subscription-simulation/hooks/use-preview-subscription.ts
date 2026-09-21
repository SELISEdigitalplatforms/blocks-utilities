import { useMutation } from "@tanstack/react-query";
import type {
  DiscountCodePreview,
  SubscribeToPlanRequest,
} from "../models/subscription-simulation.model";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";

/**
 * What subscribing would cost right now. Writes nothing, so it invalidates nothing.
 *
 * Which endpoint answers that depends on whether a discount code was typed. Without one there is
 * no verdict to give, so the plain quote is the whole answer. With one, the discount endpoint is
 * the only one that can reject a code without taking the price down with it: previewSubscription
 * throws on a bad code, which would leave a subscriber who mistyped six characters staring at an
 * error where the price they were about to pay used to be.
 *
 * Both are normalized to one shape here rather than at the call site, so the dialog reads a quote
 * and an optional verdict without caring which request produced them.
 */
export const usePreviewSubscription = () =>
  useMutation({
    mutationFn: async (
      request: SubscribeToPlanRequest,
    ): Promise<{
      status: string | null;
      message: string | null;
      quote: DiscountCodePreview["quote"];
    }> => {
      if (!request.discountCode) {
        return {
          status: null,
          message: null,
          quote: await subscriptionSimulationService.previewSubscription(request),
        };
      }

      const { status, message, quote } =
        await subscriptionSimulationService.previewDiscountCode(request);

      return { status, message, quote };
    },
  });
