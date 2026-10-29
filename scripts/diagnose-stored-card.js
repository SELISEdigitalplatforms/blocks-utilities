// Explains a `stored_payment_method_not_found` on a credit purchase or renewal: loads the
// subscription, its billing account and the account's default card, and prints the four checks
// RecurringPaymentPreflightService makes before charging a stored card. Read-only.
//
// Usage (DbConnectionString comes from BlocksRootDb.Tenants, keyed by TenantId):
//   mongosh "<tenant DbConnectionString>" scripts/diagnose-stored-card.js

// The tenant's DBName from BlocksRootDb.Tenants.
const DB_NAME = "b2e7989f865e4bd98f24475e6a5984d9";
// Test CircuitMind subscription that answered stored_payment_method_not_found on 2026-10-28.
const SUBSCRIPTION_ID = "35ac5b12-cc55-43ee-af0a-ddce25e23355";

if (DB_NAME.startsWith("REPLACE")) throw new Error("Set DB_NAME to the tenant's DBName first.");

const db = globalThis.db.getSiblingDB(DB_NAME);

const sub = db.Subscriptions.findOne({ _id: SUBSCRIPTION_ID });
if (!sub) throw new Error(`No subscription ${SUBSCRIPTION_ID} in ${DB_NAME}`);

const account = db.SubscriptionBillingAccounts.findOne({ _id: sub.BillingAccountId });
const card = account?.DefaultPaymentMethodId
  ? db.StoredPaymentMethods.findOne({ _id: account.DefaultPaymentMethodId })
  : null;

printjson({
  subscription: {
    _id: sub._id,
    OrganizationId: sub.OrganizationId,
    Status: sub.Status, // 3 = Active
    BillingAccountId: sub.BillingAccountId,
  },
  account: account && {
    _id: account._id,
    ProviderName: account.ProviderName,
    ProviderOrganizationId: account.ProviderOrganizationId,
    ProviderCustomerId: account.ProviderCustomerId,
    DefaultPaymentMethodId: account.DefaultPaymentMethodId,
  },
  defaultCard: card && {
    _id: card._id,
    OrganizationId: card.OrganizationId,
    ShopperReference: card.ShopperReference,
    Status: card.Status, // 0 = Active
    ProviderName: card.ProviderName,
    ProviderPayerReference: card.ProviderPayerReference,
  },
  // Each must be true for the preflight to accept the card. Any false is the 404.
  checks: {
    accountExists: !!account,
    accountHasDefaultCard: !!account?.DefaultPaymentMethodId,
    cardExists: !!card,
    cardOwnedBySubscriber: !!card && card.OrganizationId === sub.OrganizationId,
    shopperReferencePresent: !!card && !!card.ShopperReference,
    cardActive: !!card && card.Status === 0,
  },
});

// Every card the subscriber actually owns, so a wrong DefaultPaymentMethodId is visible.
print(`\nCards owned by organization ${sub.OrganizationId}:`);
printjson(
  db.StoredPaymentMethods.find(
    { OrganizationId: sub.OrganizationId },
    { OrganizationId: 1, ShopperReference: 1, Status: 1, ProviderName: 1, CreatedDate: 1 },
  ).toArray(),
);
