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
  SubscriptionPlan,
  UpdateSubscriptionPlanRequest,
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
  async listPlans(organizationId?: string): Promise<SubscriptionPlan[]> {
    const query = organizationId
      ? `?organizationId=${encodeURIComponent(organizationId)}`
      : "";

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
   * Takes a price off the menu. It is never edited or deleted: a price identifier is what every
   * subscription records having been sold on, so it is superseded rather than rewritten.
   * Anyone already subscribed keeps billing on their snapshot, untouched.
   */
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
