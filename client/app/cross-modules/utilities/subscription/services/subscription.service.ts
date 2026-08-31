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
  UpdateSubscriptionPriceDiscountRequest,
  UpdateSubscriptionPriceTaxRequest,
} from "../models/subscription-plan.model";

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
   */
  async listPlans(
    organizationId?: string,
    status?: PlanCatalogueFilterName,
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

    const query = parameters.size > 0 ? `?${parameters.toString()}` : "";

    const response =
      await serviceInstances.utitlitiesService.get<
        SubscriptionApiResponse<SubscriptionPlan[]>
      >(`${SUBSCRIPTION_PLANS_ENDPOINT}${query}`);

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message || "Subscription plans could not be loaded.",
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
        response.error?.message || "The plan could not be loaded.",
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
        response.error?.message || "The plan could not be created.",
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
        response.error?.message || "The plan could not be saved.",
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
        response.error?.message || "The price could not be created.",
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
        response.error?.message || "The plan could not be archived.",
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
        response.error?.message || "The price could not be retired.",
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
      throw new Error(response.error?.message || "The price tax could not be saved.");
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
        response.error?.message || "The automatic discount could not be saved.",
      );
    }

    return response.data;
  }

  async listDiscounts(organizationId?: string): Promise<SubscriptionDiscount[]> {
    const query = organizationId ? `?organizationId=${encodeURIComponent(organizationId)}` : "";
    const response = await serviceInstances.utitlitiesService.get<SubscriptionApiResponse<SubscriptionDiscount[]>>(
      `/api/subscription-discounts${query}`,
    );
    if (!response.success || !response.data) throw new Error(response.error?.message || "Discounts could not be loaded.");
    return response.data;
  }

  async createDiscount(request: CreateSubscriptionDiscountRequest): Promise<SubscriptionDiscount> {
    const response = await serviceInstances.utitlitiesService.post<SubscriptionApiResponse<SubscriptionDiscount>>(
      "/api/subscription-discounts", request,
    );
    if (!response.success || !response.data) throw new Error(response.error?.message || "The discount could not be created.");
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
    if (!response.success || !response.data) throw new Error(response.error?.message || "The discount could not be updated.");
    return response.data;
  }

  async archiveDiscount(discountId: string, organizationId?: string): Promise<SubscriptionDiscount> {
    const query = organizationId ? `?organizationId=${encodeURIComponent(organizationId)}` : "";
    const response = await serviceInstances.utitlitiesService.put<SubscriptionApiResponse<SubscriptionDiscount>>(
      `/api/subscription-discounts/${encodeURIComponent(discountId)}/archive${query}`, {},
    );
    if (!response.success || !response.data) throw new Error(response.error?.message || "The discount could not be retired.");
    return response.data;
  }
}

export const subscriptionService = new SubscriptionService();
