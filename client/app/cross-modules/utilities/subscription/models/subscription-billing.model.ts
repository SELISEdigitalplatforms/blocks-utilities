/**
 * The subscriber identity every financial document for an organization is addressed to.
 *
 * Read once when a customer reaches billing, and again after a write, so the form and the documents
 * cannot disagree about what was saved.
 */
export interface BillingAddress {
  line1?: string | null;
  line2?: string | null;
  city?: string | null;
  region?: string | null;
  postalCode?: string | null;
  /** ISO 3166-1 alpha-2. */
  countryCode?: string | null;
}

export interface SubscriptionBillingProfile {
  organizationId: string;
  legalName: string;
  displayName?: string | null;
  billingContactName: string;
  billingContactEmail: string;
  address?: BillingAddress | null;
  taxRegistrationId?: string | null;
  /**
   * Whether a paid subscription may start. False is not an error state — an organization that has
   * never answered is simply not complete yet.
   */
  isComplete: boolean;
  /**
   * Which required fields are still empty, named so a form can highlight the inputs rather than
   * showing a message it has to parse.
   */
  missingFields: string[];
  lastUpdatedDateUtc?: string | null;
}

export interface UpdateBillingProfileRequest {
  legalName: string;
  displayName?: string | null;
  billingContactName: string;
  billingContactEmail: string;
  address?: BillingAddress | null;
  taxRegistrationId?: string | null;
  /** Console only; the server honours it for nobody else. */
  organizationId?: string;
}

export type FinancialDocumentType = "Invoice" | "TrialInvoice" | "CreditNote";

export type FinancialDocumentStatus = "Issued" | "PartiallyRefunded" | "Refunded";

/**
 * Every figure on a document, in minor units.
 *
 * Each discount source is separate on purpose. "Something came off" cannot be turned back into
 * "the yearly price gave 8% and the coupon gave nothing", and which of them it was is what somebody
 * reconciling an old invoice is actually asking.
 */
export interface FinancialDocumentAmounts {
  grossSubtotalMinor: number;
  automaticDiscountMinor: number;
  quantityDiscountMinor: number;
  promotionalDiscountMinor: number;
  netSubtotalMinor: number;
  taxRateBasisPoints?: number | null;
  taxMode?: string | null;
  taxAmountMinor: number;
  /** Banked credit spent against this document. A deduction below tax, not a discount. */
  creditAppliedMinor: number;
  totalMinor: number;
  automaticDiscountBasisPoints?: number | null;
  quantityDiscountBasisPoints?: number | null;
  discountCombination?: string | null;
  promotionCode?: string | null;
}

/** One side of a plan or quantity change, priced as its own period and then prorated. */
export interface FinancialDocumentSettlementSide {
  grossAmountMinor: number;
  builtInDiscountMinor: number;
  promotionalDiscountMinor: number;
  taxAmountMinor: number;
  periodTotalMinor: number;
  proratedValueMinor: number;
}

/**
 * How a mid-period change was settled.
 *
 * Present only on a plan or quantity change, whose amount is a subtraction between two prorated
 * periods rather than a discounted price — so a single subtotal cannot explain it.
 */
export interface FinancialDocumentSettlement {
  outgoing: FinancialDocumentSettlementSide;
  target: FinancialDocumentSettlementSide;
  creditConsumedMinor: number;
  netSettlementMinor: number;
}

export interface FinancialDocumentLine {
  description: string;
  quantity?: number | null;
  unitAmountMinor?: number | null;
  amountMinor: number;
  itemKey?: string | null;
}

export interface FinancialDocumentTrial {
  startsAtUtc: string;
  endsAtUtc: string;
  requiresPaymentMethod: boolean;
  firstBillingAtUtc?: string | null;
}

export interface SubscriptionFinancialDocument {
  documentId: string;
  documentNumber: string;
  documentType: FinancialDocumentType;
  status: FinancialDocumentStatus;
  issuedAtUtc: string;
  subscriptionId: string;
  currencyCode: string;
  planCode: string;
  planName: string;
  periodStartUtc?: string | null;
  periodEndUtc?: string | null;
  /** Calendar dates in the subscriber's own zone, formatted when the document was issued. */
  periodLocalStart?: string | null;
  periodLocalEnd?: string | null;
  timeZoneId?: string | null;
  amounts: FinancialDocumentAmounts;
  settlement?: FinancialDocumentSettlement | null;
  lines: FinancialDocumentLine[];
  trial?: FinancialDocumentTrial | null;
  /**
   * The parties as the document states them, not as the profile stands now. A page rendering
   * "billed to" has to show these, or it will disagree with the PDF the moment somebody edits.
   */
  subscriberLegalName: string;
  billingContactName: string;
  billingContactEmail?: string | null;
  initiatedByName: string;
  initiatedByUserId?: string | null;
  paymentDetailId?: string | null;
  refundId?: string | null;
  originalDocumentId?: string | null;
  originalDocumentNumber?: string | null;
  /** Whether the PDF exists, so a download control can be rendered without probing for a 404. */
  isPdfAvailable: boolean;
  pdfContentHash?: string | null;
  downloadUrl: string;
}

export interface FinancialDocumentPageInfo {
  pageSize: number;
  hasNextPage: boolean;
  nextCursor?: string | null;
}

export interface SubscriptionFinancialDocumentPage {
  items: SubscriptionFinancialDocument[];
  pageInfo: FinancialDocumentPageInfo;
}

export interface FinancialDocumentQuery {
  pageSize?: number;
  after?: string | null;
  subscriptionId?: string | null;
  documentType?: FinancialDocumentType | null;
  status?: FinancialDocumentStatus | null;
  issuedFromUtc?: string | null;
  issuedToUtc?: string | null;
  organizationId?: string;
}
