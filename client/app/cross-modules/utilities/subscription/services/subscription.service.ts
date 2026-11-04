import { serviceInstances } from "@/lib/http-client";
import {
  SUBSCRIPTION_PLAN_PRICES_ENDPOINT,
  SUBSCRIPTION_PLANS_ENDPOINT,
} from "../constants/subscription.constants";
import type {
  CreateSubscriptionPlanRequest,
  CreateSubscriptionPriceRequest,
  CreateSubscriptionDiscountRequest,
  SubscriptionDiscount,
  PlanCatalogueFilterName,
  SubscriptionPlan,
  UpdateSubscriptionPlanRequest,
  UpdateSubscriptionDiscountRequest,
  UpdateSubscriptionPlanMeterRatesRequest,
  UpdateSubscriptionPriceDiscountRequest,
  UpdateSubscriptionPriceTaxRequest,
} from "../models/subscription-plan.model";
import { tSubscription } from "../utilities/subscription-i18n";

interface SubscriptionApiError {
  code: string;
  message: string;
  fields?: Record<string, string[]> | null;
}

interface SubscriptionApiResponse<T> {
  success: boolean;
  data: T | null;
  error: SubscriptionApiError | null;
}

class SubscriptionService {
  /**
   * @param status Which plans to ask for. Omitted by every subscriber-facing caller, which is
   * what makes the default — the active catalogue — the thing those screens receive.
   * @param familyCode Narrows the listing to one product family, ordered by family rank. Omit it
   * for every family. Matched exactly and case-sensitively by the server, so it must be sent as
   * the plan authored it.
   */
  async listPlans(
    organizationId?: string,
    status?: PlanCatalogueFilterName,
    familyCode?: string,
  ): Promise<SubscriptionPlan[]> {
    const parameters = new URLSearchParams();

    if (organizationId) {
      parameters.set("organizationId", organizationId);
    }

    // Left off entirely rather than sent as "Active", so the request a subscriber-facing screen
    // makes is byte-identical to the one it made before this parameter existed.
    if (status && status !== "Active") {
      parameters.set("status", status);
    }

    // Trimmed only to decide whether it was supplied at all: a blank field is not a family, and
    // the server reads blank as "every family" anyway. The value itself is sent untrimmed, because
    // the server matches it exactly against what the plan was authored with.
    if (familyCode && familyCode.trim().length > 0) {
      parameters.set("familyCode", familyCode);
    }

    const query = parameters.size > 0 ? `?${parameters.toString()}` : "";

    const response =
      await serviceInstances.utitlitiesService.get<
        SubscriptionApiResponse<SubscriptionPlan[]>
      >(`${SUBSCRIPTION_PLANS_ENDPOINT}${query}`);

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message || tSubscription("SUBSCRIPTION_SERVICE.SUBSCRIPTION_PLANS_COULD_NOT_BE_LOADED"),
      );
    }

    return response.data;
  }

  async getPlan(
    planId: string,
    organizationId?: string,
  ): Promise<SubscriptionPlan> {
    const query = organizationId
      ? `?organizationId=${encodeURIComponent(organizationId)}`
      : "";

    const response =
      await serviceInstances.utitlitiesService.get<
        SubscriptionApiResponse<SubscriptionPlan>
      >(`${SUBSCRIPTION_PLANS_ENDPOINT}/${encodeURIComponent(planId)}${query}`);

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message || tSubscription("SUBSCRIPTION_SERVICE.PLAN_COULD_NOT_BE_LOADED"),
      );
    }

    return response.data;
  }

  async createPlan(
    request: CreateSubscriptionPlanRequest,
  ): Promise<SubscriptionPlan> {
    const response =
      await serviceInstances.utitlitiesService.post<
        SubscriptionApiResponse<SubscriptionPlan>
      >(SUBSCRIPTION_PLANS_ENDPOINT, request);

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message || tSubscription("SUBSCRIPTION_SERVICE.PLAN_COULD_NOT_BE_CREATED"),
      );
    }

    return response.data;
  }

  async updatePlan(
    planId: string,
    request: UpdateSubscriptionPlanRequest,
  ): Promise<SubscriptionPlan> {
    const response =
      await serviceInstances.utitlitiesService.put<
        SubscriptionApiResponse<SubscriptionPlan>
      >(`${SUBSCRIPTION_PLANS_ENDPOINT}/${encodeURIComponent(planId)}`, request);

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message || tSubscription("SUBSCRIPTION_SERVICE.PLAN_COULD_NOT_BE_SAVED"),
      );
    }

    return response.data;
  }

  async createPrice(
    request: CreateSubscriptionPriceRequest,
  ): Promise<SubscriptionPlan> {
    const response =
      await serviceInstances.utitlitiesService.post<
        SubscriptionApiResponse<SubscriptionPlan>
      >(SUBSCRIPTION_PLAN_PRICES_ENDPOINT, request);

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message || tSubscription("SUBSCRIPTION_SERVICE.PRICE_COULD_NOT_BE_CREATED"),
      );
    }

    return response.data;
  }

  /**
   * Takes a price off the menu. Its commercial terms are never edited or deleted: a price identifier is what every
   * subscription records having been sold on, so it is superseded rather than rewritten.
   * Anyone already subscribed keeps billing on their snapshot, untouched.
   */
  /**
   * Takes a whole plan off the menu, permanently.
   *
   * Existing subscribers are untouched and none of the plan's prices is rewritten: each bills from
   * the snapshot copied onto it when it was sold. Renewal, usage rating, entitlements, invoicing
   * and cancellation all continue. What stops is selling, and every further change to the plan.
   *
   * Safe to repeat: a second call returns the archived plan without writing again, so a
   * double-submitted dialog cannot produce an error about work that already succeeded. There is no
   * restore — a replacement is made by duplicating the plan.
   */
  async archivePlan(
    planId: string,
    organizationId?: string,
  ): Promise<SubscriptionPlan> {
    const query = organizationId
      ? `?organizationId=${encodeURIComponent(organizationId)}`
      : "";
    const response =
      await serviceInstances.utitlitiesService.put<
        SubscriptionApiResponse<SubscriptionPlan>
      >(
        `${SUBSCRIPTION_PLANS_ENDPOINT}/${encodeURIComponent(planId)}/archive${query}`,
        {},
      );

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message || tSubscription("SUBSCRIPTION_SERVICE.PLAN_COULD_NOT_BE_ARCHIVED"),
      );
    }

    return response.data;
  }

  async archivePrice(
    priceId: string,
    organizationId?: string,
  ): Promise<SubscriptionPlan> {
    const query = organizationId
      ? `?organizationId=${encodeURIComponent(organizationId)}`
      : "";
    const response =
      await serviceInstances.utitlitiesService.put<
        SubscriptionApiResponse<SubscriptionPlan>
      >(
        `${SUBSCRIPTION_PLAN_PRICES_ENDPOINT}/${encodeURIComponent(priceId)}/archive${query}`,
        {},
      );

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message || tSubscription("SUBSCRIPTION_SERVICE.PRICE_COULD_NOT_BE_RETIRED"),
      );
    }

    return response.data;
  }

  async updatePriceTax(
    priceId: string,
    request: UpdateSubscriptionPriceTaxRequest,
  ): Promise<SubscriptionPlan> {
    const response = await serviceInstances.utitlitiesService.put<
      SubscriptionApiResponse<SubscriptionPlan>
    >(`${SUBSCRIPTION_PLAN_PRICES_ENDPOINT}/${encodeURIComponent(priceId)}/tax`, request);

    if (!response.success || !response.data) {
      throw new Error(response.error?.message || tSubscription("SUBSCRIPTION_SERVICE.PRICE_TAX_COULD_NOT_BE_SAVED"));
    }

    return response.data;
  }

  /**
   * Rewrites one meter's overage rate tables. Unlike {@link updatePlan}, not refused once the
   * plan has subscribers — overage is rated from the meter snapshot copied onto a subscription at
   * signup, so this reaches only future subscriptions and future renewals.
   */
  async updatePlanMeterRates(
    planId: string,
    meterKey: string,
    request: UpdateSubscriptionPlanMeterRatesRequest,
  ): Promise<SubscriptionPlan> {
    const response = await serviceInstances.utitlitiesService.put<
      SubscriptionApiResponse<SubscriptionPlan>
    >(
      `${SUBSCRIPTION_PLANS_ENDPOINT}/${encodeURIComponent(planId)}/meters/${encodeURIComponent(meterKey)}/rates`,
      request,
    );

    if (!response.success || !response.data) {
      throw new Error(response.error?.message || tSubscription("SUBSCRIPTION_SERVICE.OVERAGE_RATES_COULD_NOT_BE_SAVED"));
    }

    return response.data;
  }

  async updatePriceDiscount(
    priceId: string,
    request: UpdateSubscriptionPriceDiscountRequest,
  ): Promise<SubscriptionPlan> {
    const response = await serviceInstances.utitlitiesService.put<
      SubscriptionApiResponse<SubscriptionPlan>
    >(`${SUBSCRIPTION_PLAN_PRICES_ENDPOINT}/${encodeURIComponent(priceId)}/discount`, request);

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message || tSubscription("SUBSCRIPTION_SERVICE.AUTOMATIC_DISCOUNT_COULD_NOT_BE_SAVED"),
      );
    }

    return response.data;
  }

  async listDiscounts(organizationId?: string): Promise<SubscriptionDiscount[]> {
    const query = organizationId ? `?organizationId=${encodeURIComponent(organizationId)}` : "";
    const response = await serviceInstances.utitlitiesService.get<SubscriptionApiResponse<SubscriptionDiscount[]>>(
      `/api/subscription-discounts${query}`,
    );
    if (!response.success || !response.data) throw new Error(response.error?.message || tSubscription("SUBSCRIPTION_SERVICE.DISCOUNTS_COULD_NOT_BE_LOADED"));
    return response.data;
  }

  async createDiscount(request: CreateSubscriptionDiscountRequest): Promise<SubscriptionDiscount> {
    const response = await serviceInstances.utitlitiesService.post<SubscriptionApiResponse<SubscriptionDiscount>>(
      "/api/subscription-discounts", request,
    );
    if (!response.success || !response.data) throw new Error(response.error?.message || tSubscription("SUBSCRIPTION_SERVICE.DISCOUNT_COULD_NOT_BE_CREATED"));
    return response.data;
  }

  async updateDiscount(
    discountId: string,
    request: UpdateSubscriptionDiscountRequest,
    organizationId?: string,
  ): Promise<SubscriptionDiscount> {
    const query = organizationId ? `?organizationId=${encodeURIComponent(organizationId)}` : "";
    const response = await serviceInstances.utitlitiesService.put<SubscriptionApiResponse<SubscriptionDiscount>>(
      `/api/subscription-discounts/${encodeURIComponent(discountId)}${query}`,
      request,
    );
    if (!response.success || !response.data) throw new Error(response.error?.message || tSubscription("SUBSCRIPTION_SERVICE.DISCOUNT_COULD_NOT_BE_UPDATED"));
    return response.data;
  }

  async archiveDiscount(discountId: string, organizationId?: string): Promise<SubscriptionDiscount> {
    const query = organizationId ? `?organizationId=${encodeURIComponent(organizationId)}` : "";
    const response = await serviceInstances.utitlitiesService.put<SubscriptionApiResponse<SubscriptionDiscount>>(
      `/api/subscription-discounts/${encodeURIComponent(discountId)}/archive${query}`, {},
    );
    if (!response.success || !response.data) throw new Error(response.error?.message || tSubscription("SUBSCRIPTION_SERVICE.DISCOUNT_COULD_NOT_BE_RETIRED"));
    return response.data;
  }
}

export const subscriptionService = new SubscriptionService();
