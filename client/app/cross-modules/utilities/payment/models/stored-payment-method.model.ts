export interface StoredPaymentMethod {
  paymentMethodId: string;
  type: string;
  brand: string | null;
  lastFour: string | null;
  expiryMonth: string | null;
  expiryYear: string | null;
  fundingSource: string | null;
  issuerCountry: string | null;
  status: string;
}

export interface StoredPaymentMethodRemovalResponse {
  paymentMethodId: string;
  status: "REMOVAL_PENDING";
}

export type StoredPaymentMethodRemovalOutcome =
  | "removed"
  | "pending";
