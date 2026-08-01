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
