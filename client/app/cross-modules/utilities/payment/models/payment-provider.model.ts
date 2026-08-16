export const PAYMENT_PROVIDER_NAMES = [
  "ADYEN-ONLINE",
  "STRIPE",
] as const;

export type PaymentProviderName =
  (typeof PAYMENT_PROVIDER_NAMES)[number];

export interface PaymentProvider {
  paymentProviderId: string;
  version: number;
  providerName: string;
  merchantId: string;
  organizationId: string | null;
  apiBaseUrl: string;
  returnUrl: string | null;
  frontendResultUrl: string | null;
  countryCode: string | null;
  manualCapture: boolean;
  maxRefundDays: number;
  storeId: string | null;
  isEnabled: boolean;
}

export interface RegisterPaymentProviderRequest {
  providerName: PaymentProviderName;
  merchantId: string;
  /** Omitted entirely when the caller's own organization should be used. */
  organizationId?: string;
  /**
   * Several organizations to configure identically. Unioned with `organizationId` and
   * de-duplicated by the server; each gets its own row and its own key ring.
   */
  organizationIds?: string[];
  frontendResultUrl: string;
  apiBaseUrl?: string;
  countryCode?: string;
  manualCapture: boolean;
  maxRefundDays: number;
  storeId?: string;
  apiKey: string;
  webhookHmacKey: string;
  tokenHmacKey?: string;
}

export interface RegisteredPaymentProvider {
  paymentDetailId: string;
  providerName: string;
  paymentStatus: "REGISTERED";
}

/** What happened for one organization when several were configured at once. */
export interface PaymentProviderRegistrationOutcome {
  organizationId: string | null;
  isSuccess: boolean;
  status: "REGISTERED" | "FAILED";
  paymentProviderId: string | null;
  errorCode: string | null;
  errorMessage: string | null;
}

/** The body the server sends when a registration named more than one organization. */
export interface PaymentProviderRegistrationResponse {
  providerName: string;
  organizations: PaymentProviderRegistrationOutcome[];
}

/**
 * The result of registering across one or more organizations, normalised so callers do not have
 * to know which of the two response shapes the server used.
 */
export interface PaymentProviderRegistrationResult {
  providerName: string;
  organizations: PaymentProviderRegistrationOutcome[];
  /** False when at least one organization failed; the others were still written. */
  allSucceeded: boolean;
}

export interface UpdatePaymentProviderRequest {
  version: number;
  frontendResultUrl: string;
  countryCode?: string;
  manualCapture: boolean;
  maxRefundDays: number;
  storeId?: string;
  isEnabled: boolean;
}

export interface RotatePaymentProviderCredentialsRequest {
  version: number;
  apiKey?: string;
  webhookHmacKey?: string;
  tokenHmacKey?: string;
}

export interface UpdatePaymentProviderCommand {
  paymentProviderId: string;
  request: UpdatePaymentProviderRequest;
}

export interface RotatePaymentProviderCredentialsCommand {
  paymentProviderId: string;
  request: RotatePaymentProviderCredentialsRequest;
}
