export interface CreatePaymentRefundRequest {
  amount: number;
  reason?: string;
}

export interface CreatePaymentRefundCommand {
  paymentDetailId: string;
  request: CreatePaymentRefundRequest;
  idempotencyKey: string;
}

export interface PaymentRefund {
  refundId: string;
  paymentDetailId: string;
  status: string;
  amount: number;
  currencyCode: string;
  operation: string;
  completionAction: string | null;
  failureCode: string | null;
  failureSummary: string | null;
  createdAtUtc: string;
  completedAtUtc: string | null;
}
