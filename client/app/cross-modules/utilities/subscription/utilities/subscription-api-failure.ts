/**
 * The server's own account of why a subscription call failed.
 *
 * Kept as a code, a sentence and a field map rather than flattened to a string, because the code is
 * what tells one refusal from another and the fields are what tell somebody how to fix it. Reduced
 * to `error.message`, an incomplete billing profile and a declined card read as the same kind of
 * sorry.
 */
export interface SubscriptionApiFailure {
  code: string;
  message: string;
  fields: Record<string, string[]>;
}

/** The failure codes this module acts on rather than merely reports. */
export const BILLING_PROFILE_INCOMPLETE = "subscription_billing_profile_incomplete";

/**
 * Which field list the server returns the missing profile details under.
 *
 * One key holding the whole list, rather than one key per field: the server is answering "what is
 * still needed", not "what did you type wrongly", and the subscriber has not typed anything yet.
 */
const PROFILE_FIELD_KEY = "BillingProfile";

/**
 * The tenant's own selling identity, which the same list can carry.
 *
 * Named apart from the subscriber's fields because the two are fixed by different people on
 * different screens. Telling a subscriber to add a legal name, when the missing name is the
 * platform's own, sends them to correct something that was never theirs.
 */
const MERCHANT_FIELD = "merchantLegalName";

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === "object" && value !== null;

const asFields = (value: unknown): Record<string, string[]> => {
  if (!isRecord(value)) {
    return {};
  }

  return Object.fromEntries(
    Object.entries(value).map(([key, entry]) => [
      key,
      Array.isArray(entry) ? entry.map(String) : [String(entry)],
    ]),
  );
};

/**
 * Digs the API's error envelope out of whatever the transport wrapped it in.
 *
 * Every failing status arrives as an `HttpError` whose `errors` is the parsed response body, so the
 * envelope sits one level down; a failure the client noticed itself has no envelope at all. Both
 * shapes are looked at rather than one being assumed, because assuming the wrong one turns every
 * named refusal into "something went wrong" — and the whole point of a code is that the client can
 * do something specific with it.
 */
export const subscriptionApiFailure = (error: unknown): SubscriptionApiFailure | null => {
  const candidates: unknown[] = [];

  if (isRecord(error)) {
    candidates.push(error.error);

    if (isRecord(error.errors)) {
      candidates.push(error.errors.error, error.errors);
    }

    candidates.push(error);
  }

  for (const candidate of candidates) {
    if (isRecord(candidate) && typeof candidate.code === "string" && candidate.code) {
      return {
        code: candidate.code,
        message: typeof candidate.message === "string" ? candidate.message : "",
        fields: asFields(candidate.fields),
      };
    }
  }

  return null;
};

/**
 * What an organization still owes before it can be charged, split by who can supply it.
 *
 * Null when the failure was something else, so a caller can fall back to reporting the message.
 */
export interface BillingProfileGap {
  /** Fields on the subscriber's own billing profile. */
  subscriberFields: string[];
  /** Whether the tenant has never named itself as the seller. */
  merchantMissing: boolean;
}

/**
 * @param failure
 * A refused call's envelope, or a preview's blocker. The two carry the same code and the same field
 * map — one as the reason nothing happened, the other as a warning alongside a price — so they
 * are read here by one function rather than two that could come to disagree.
 */
export const billingProfileGapOf = (
  failure: { code: string; fields?: Record<string, string[]> | null } | null,
): BillingProfileGap | null => {
  if (failure?.code !== BILLING_PROFILE_INCOMPLETE) {
    return null;
  }

  const missing = failure.fields?.[PROFILE_FIELD_KEY] ?? [];

  return {
    subscriberFields: missing.filter((field) => field !== MERCHANT_FIELD),
    merchantMissing: missing.includes(MERCHANT_FIELD),
  };
};
