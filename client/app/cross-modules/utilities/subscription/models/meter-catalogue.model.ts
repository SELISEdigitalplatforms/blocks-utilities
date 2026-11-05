/** `GET /api/meter-catalogue`. Identity and counting semantics only; see spec 005. */
export interface MeterCatalogueEntry {
  entryId: string;
  /** Null for the tenant-wide entry; otherwise the organization it overrides for. */
  organizationId: string | null;
  meterKey: string;
  displayName: string;
  unitLabel: string;
  aggregation: "Sum" | "Max" | "Last";
  quantityScale: number;
  status: "Active" | "Archived";
  createdAtUtc: string;
  lastUpdatedAtUtc: string;
  version: number;
}

export interface CreateMeterCatalogueEntryRequest {
  organizationId?: string;
  meterKey: string;
  displayName: string;
  unitLabel: string;
  /** Numeric, as the plan builder sends it: 0 Sum, 1 Max, 2 Last. */
  aggregation: number;
  quantityScale: number;
}

export interface UpdateMeterCatalogueEntryRequest {
  displayName: string;
  unitLabel: string;
  aggregation: number;
  quantityScale: number;
  status: "Active" | "Archived";
}

export type CreditSpendOrder = "SubscriptionFirst" | "OrganizationCreditsFirst";

export interface CreditOfferPrice {
  currencyCode: string;
  unitAmountMinor: number;
}

export interface CreditExpiryDuration {
  count: number;
  unit: "Hour" | "Day" | "Week" | "Month" | "Year";
}

/** `GET /api/credit-offers`. One per meter per scope; see spec 005. */
export interface CreditOffer {
  offerId: string;
  organizationId: string | null;
  meterKey: string;
  displayName: string;
  unitLabel: string;
  enabled: boolean;
  unitPrices: CreditOfferPrice[];
  minQuantity: number;
  maxQuantity: number;
  expiresAfter: CreditExpiryDuration | null;
  taxRateBasisPoints: number | null;
  taxMode: "Exclusive" | "Inclusive" | null;
  spendOrder: CreditSpendOrder;
  createdAtUtc: string;
  lastUpdatedAtUtc: string;
  version: number;
}

export interface CreditOfferRequest {
  organizationId?: string;
  meterKey: string;
  enabled: boolean;
  unitPrices: CreditOfferPrice[];
  minQuantity: number;
  maxQuantity: number;
  expiresAfter: CreditExpiryDuration | null;
  taxRateBasisPoints?: number | null;
  taxMode?: "Exclusive" | "Inclusive" | null;
  spendOrder: CreditSpendOrder;
}
