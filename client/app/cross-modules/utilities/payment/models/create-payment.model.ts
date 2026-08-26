import type { PaymentProviderName } from "./payment-provider.model";

export interface CreatePaymentRequest {
  providerName: PaymentProviderName;
  amount: number;
  currencyCode: string;
  orderId: string;
  rememberCard: boolean;
  isRecurring: false;
  /**
   * Which organization's merchant account takes the money. Omitted entirely when the
   * caller's own organization should be used.
   */
  organizationId?: string;
}

export interface CreatedPayment {
  paymentDetailId: string;
  providerName: string;
  paymentStatus: string;
  orderId: string | null;
  amount: number;
  currencyCode: string;
  redirectUrl: string | null;
  expiresAtUtc: string | null;
}

export interface CreatePaymentCommand {
  request: CreatePaymentRequest;
  idempotencyKey: string;
}
