import { serviceInstances } from "@/lib/http-client";
import type {
  CreateMeterCatalogueEntryRequest,
  CreditOffer,
  CreditOfferRequest,
  MeterCatalogueEntry,
  UpdateMeterCatalogueEntryRequest,
} from "../models/meter-catalogue.model";
import { tSubscription } from "../utilities/subscription-i18n";

export const METER_CATALOGUE_ENDPOINT = "/api/meter-catalogue";
export const CREDIT_OFFERS_ENDPOINT = "/api/credit-offers";

interface ApiResponse<T> {
  success: boolean;
  data: T | null;
  error: { code: string; message: string } | null;
}

const unwrap = <T>(response: ApiResponse<T>, fallbackKey: string): T => {
  if (!response.success || response.data === null) {
    throw new Error(response.error?.message || tSubscription(fallbackKey));
  }
  return response.data;
};

const scope = (organizationId?: string) =>
  organizationId ? `?organizationId=${encodeURIComponent(organizationId)}` : "";

/** The meter catalogue and the standalone credit offers (spec 005). */
export const meterCatalogueService = {
  async listEntries(organizationId?: string): Promise<MeterCatalogueEntry[]> {
    return unwrap(
      await serviceInstances.utitlitiesService.get<ApiResponse<MeterCatalogueEntry[]>>(
        `${METER_CATALOGUE_ENDPOINT}${scope(organizationId)}`,
      ),
      "METER_CATALOGUE_SERVICE.CATALOGUE_COULD_NOT_BE_LOADED",
    );
  },

  async createEntry(request: CreateMeterCatalogueEntryRequest): Promise<MeterCatalogueEntry> {
    return unwrap(
      await serviceInstances.utitlitiesService.post<ApiResponse<MeterCatalogueEntry>>(
        METER_CATALOGUE_ENDPOINT,
        request,
      ),
      "METER_CATALOGUE_SERVICE.METER_COULD_NOT_BE_SAVED",
    );
  },

  async updateEntry(
    meterKey: string,
    request: UpdateMeterCatalogueEntryRequest,
    organizationId?: string,
  ): Promise<MeterCatalogueEntry> {
    return unwrap(
      await serviceInstances.utitlitiesService.put<ApiResponse<MeterCatalogueEntry>>(
        `${METER_CATALOGUE_ENDPOINT}/${encodeURIComponent(meterKey)}${scope(organizationId)}`,
        request,
      ),
      "METER_CATALOGUE_SERVICE.METER_COULD_NOT_BE_SAVED",
    );
  },

  async listOffers(organizationId?: string): Promise<CreditOffer[]> {
    return unwrap(
      await serviceInstances.utitlitiesService.get<ApiResponse<CreditOffer[]>>(
        `${CREDIT_OFFERS_ENDPOINT}${scope(organizationId)}`,
      ),
      "METER_CATALOGUE_SERVICE.OFFERS_COULD_NOT_BE_LOADED",
    );
  },

  async createOffer(request: CreditOfferRequest): Promise<CreditOffer> {
    return unwrap(
      await serviceInstances.utitlitiesService.post<ApiResponse<CreditOffer>>(
        CREDIT_OFFERS_ENDPOINT,
        request,
      ),
      "METER_CATALOGUE_SERVICE.OFFER_COULD_NOT_BE_SAVED",
    );
  },

  async updateOffer(
    meterKey: string,
    request: CreditOfferRequest,
    organizationId?: string,
  ): Promise<CreditOffer> {
    return unwrap(
      await serviceInstances.utitlitiesService.put<ApiResponse<CreditOffer>>(
        `${CREDIT_OFFERS_ENDPOINT}/${encodeURIComponent(meterKey)}${scope(organizationId)}`,
        request,
      ),
      "METER_CATALOGUE_SERVICE.OFFER_COULD_NOT_BE_SAVED",
    );
  },
};
