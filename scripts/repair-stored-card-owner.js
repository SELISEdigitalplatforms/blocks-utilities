// Repairs billing accounts whose default card is stored under the merchant scope instead of the
// subscriber's organization. RecurringPaymentPreflightService refuses such a card with
// stored_payment_method_not_found, so every credit purchase and renewal on the account fails.
//
// Each card was saved by its subscriber's own checkout, so the fix is to record the subscriber
// as its owner. Before writing, ownership is proven from the checkout payment: the payment for
// the subscription's order must carry the same shopper reference as the card and name the
// subscriber as PaymentMethodOwnerOrganizationId. A card that fails the proof is listed and
// left alone.
//
// Sweeps every live subscription in the tenant. Running it again is a no-op once all are owned.
//
// Usage (DbConnectionString comes from BlocksRootDb.Tenants, keyed by TenantId):
//   mongosh "<tenant DbConnectionString>" scripts/repair-stored-card-owner.js                          # dry run
//   mongosh "<tenant DbConnectionString>" --eval "const DRY_RUN=false" scripts/repair-stored-card-owner.js
//
// Restore from the backup it writes:
//   EJSON.parse(fs.readFileSync("<backup file>", "utf8")).forEach(d => db.StoredPaymentMethods.replaceOne({ _id: d._id }, d))

const DB_NAME = "b2e7989f865e4bd98f24475e6a5984d9";
const LIVE = [2, 3, 4, 5]; // SubscriptionStatus: Trialing, Active, PastDue, Unpaid

const dryRun = typeof DRY_RUN === "undefined" ? true : DRY_RUN;
const db = globalThis.db.getSiblingDB(DB_NAME);
const BACKUP_DIR = `${os.homedir()}/blocks-backups`;
const BACKUP_FILE = `${BACKUP_DIR}/stored-card-owner-${DB_NAME}-${new Date().toISOString().replace(/[:.]/g, "-")}.json`;

const repairable = []; // { sub, card }
const refused = [];    // { subscription, card, proof }

db.Subscriptions.find({ Status: { $in: LIVE } }).forEach((sub) => {
  const account = db.SubscriptionBillingAccounts.findOne({ _id: sub.BillingAccountId });
  if (!account?.DefaultPaymentMethodId) return;
  const card = db.StoredPaymentMethods.findOne({ _id: account.DefaultPaymentMethodId });
  if (!card || card.OrganizationId === sub.OrganizationId) return;

  // Proof the card is this subscriber's: the checkout that paid for the subscription saved it.
  const payment = db.PaymentDetails.findOne({ OrderId: sub.OrderId, ShopperReference: card.ShopperReference });
  const proof = {
    paymentFound: !!payment,
    paymentNamesSubscriberAsOwner: payment?.PaymentMethodOwnerOrganizationId === sub.OrganizationId,
    cardIsActive: card.Status === 0,
    cardUnderMerchantScope: card.OrganizationId === (account.ProviderOrganizationId ?? "default"),
  };

  if (Object.values(proof).every(Boolean)) {
    repairable.push({ sub, card });
  } else {
    refused.push({ subscription: sub._id, subscriberOrg: sub.OrganizationId, card: card._id, cardOrg: card.OrganizationId, proof });
  }
});

print(`${repairable.length} cards proven to belong to their subscriber; ${refused.length} refused.`);
repairable.forEach(({ sub, card }) =>
  print(`  ${dryRun ? "[dry] " : ""}${sub._id}: card ${card._id} ${card.OrganizationId} -> ${sub.OrganizationId}`));

if (refused.length > 0) {
  print("\nLeft alone (ownership not proven from the checkout payment):");
  printjson(refused);
}

if (!dryRun && repairable.length > 0) {
  fs.mkdirSync(BACKUP_DIR, { recursive: true });
  fs.writeFileSync(BACKUP_FILE, EJSON.stringify(repairable.map((r) => r.card), null, 2));
  print(`\nBacked up ${repairable.length} cards to ${BACKUP_FILE}`);

  let modified = 0;
  for (const { sub, card } of repairable) {
    const r = db.StoredPaymentMethods.updateOne(
      { _id: card._id, OrganizationId: card.OrganizationId },
      { $set: { OrganizationId: sub.OrganizationId, UpdatedAtUtc: new Date() } },
    );
    modified += r.modifiedCount;
  }
  print(`Re-owned ${modified} of ${repairable.length}.`);
} else if (dryRun) {
  print("\nDry run, nothing written.");
}
