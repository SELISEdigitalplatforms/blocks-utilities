// Copies each active plan's meter TopUp terms onto the live subscriptions still holding a
// snapshot without them. Plan edits never reach existing subscriptions (see
// Subscription.DomainService/README.md, "snapshot rule"), so credits authored after a
// subscriber signed up are "not for sale" to them until this runs.
//
// TopUp is read only by the credit purchase path and the response mapper; it changes nothing
// about how a meter counts or bills, which is why this is safe to patch in place.
//
// Every subscription about to be touched is written, unchanged, to BACKUP_FILE (Extended JSON,
// so dates and numeric types survive the round trip) before the first write. To restore:
//   EJSON.parse(fs.readFileSync(BACKUP_FILE, "utf8")).forEach(d => db.Subscriptions.replaceOne({ _id: d._id }, d))
//
// Usage (the tenant's DBName and DbConnectionString come from BlocksRootDb.Tenants, keyed by TenantId):
//   mongosh "<tenant DbConnectionString>" scripts/backfill-topup-terms.js                       # dry run
//   mongosh "<tenant DbConnectionString>" --eval "const DRY_RUN=false" scripts/backfill-topup-terms.js

const DB_NAME = "b2e7989f865e4bd98f24475e6a5984d9";
const BACKUP_DIR = `${os.homedir()}/blocks-backups`;
const BACKUP_FILE = `${BACKUP_DIR}/subscriptions-backup-topup-${DB_NAME}-${new Date().toISOString().replace(/[:.]/g, "-")}.json`;
const LIVE = [2, 3, 4, 5]; // SubscriptionStatus: Trialing, Active, PastDue, Unpaid
const PLAN_ACTIVE = 1;      // CatalogueStatus.Active

if (DB_NAME.startsWith("REPLACE")) throw new Error("Set DB_NAME to the tenant's DBName first.");
const dryRun = typeof DRY_RUN === "undefined" ? true : DRY_RUN;
const db = globalThis.db.getSiblingDB(DB_NAME);

const work = [];
for (const plan of db.SubscriptionPlans.find({ Status: PLAN_ACTIVE })) {
  for (const meter of plan.Meters.filter(m => m.TopUp && m.TopUp.Enabled)) {
    work.push({
      label: `${plan.Code} / ${meter.MeterKey}`,
      topUp: meter.TopUp,
      filter: {
        "Plan.PlanId": plan._id,
        Status: { $in: LIVE },
        "Plan.Meters": { $elemMatch: { MeterKey: meter.MeterKey, TopUp: null } },
      },
      options: { arrayFilters: [{ "m.MeterKey": meter.MeterKey }] },
    });
  }
}

const affected = work.length === 0 ? [] : db.Subscriptions.find({ $or: work.map(w => w.filter) }).toArray();
print(`${work.length} plan/meter pairs sell credits; ${affected.length} subscriptions lack the terms`);
work.forEach(w => print(`  ${w.label}: ${db.Subscriptions.countDocuments(w.filter)}`));

if (dryRun || affected.length === 0) {
  print(dryRun ? "Dry run, nothing written." : "Nothing to do.");
} else {
  fs.mkdirSync(BACKUP_DIR, { recursive: true });
  fs.writeFileSync(BACKUP_FILE, EJSON.stringify(affected, null, 2));
  print(`Backed up ${affected.length} subscriptions to ${BACKUP_FILE}`);

  for (const w of work) {
    const r = db.Subscriptions.updateMany(
      w.filter,
      // Bump the CAS version so any in-flight write that read the old snapshot loses and re-reads.
      { $set: { "Plan.Meters.$[m].TopUp": w.topUp }, $inc: { Version: 1 } },
      w.options);
    print(`  ${w.label}: matched ${r.matchedCount}, modified ${r.modifiedCount}`);
  }
}
