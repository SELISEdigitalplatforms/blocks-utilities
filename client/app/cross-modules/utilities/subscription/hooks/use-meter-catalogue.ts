import { useQuery } from "@tanstack/react-query";
import { meterCatalogueService } from "../services/meter-catalogue.service";

export const METER_CATALOGUE_QUERY_KEY = "meter-catalogue";
export const CREDIT_OFFERS_QUERY_KEY = "credit-offers";

export const useMeterCatalogue = (organizationId?: string) =>
  useQuery({
    queryKey: [METER_CATALOGUE_QUERY_KEY, organizationId ?? null],
    queryFn: () => meterCatalogueService.listEntries(organizationId),
  });

export const useCreditOffers = (organizationId?: string) =>
  useQuery({
    queryKey: [CREDIT_OFFERS_QUERY_KEY, organizationId ?? null],
    queryFn: () => meterCatalogueService.listOffers(organizationId),
  });
