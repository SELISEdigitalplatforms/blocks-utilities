// Records the IAM organization name on subscriptions created before signup captured it, so the
// invoices and credit notes issued for them from now on print "Organization: <name>" instead of
// the bare organization id. Documents already issued keep what they were issued with.
//
// The name comes straight from IAM's Organizations collection, the way signup would have asked
// for it. "default" is a scope sentinel in IAM with no row there; it is named "Default", the
// label IAM and the blocks-os pickers already give it. A subscription whose organization IAM
// does not hold (deleted, or a different database) is reported and left alone.
//
// Only writes OrganizationName, and only where it is unset. To undo:
//   db.Subscriptions.updateMany({}, { $unset: { OrganizationName: "" } })
//
// Usage (the tenant's DBName and DbConnectionString come from BlocksRootDb.Tenants, keyed by TenantId):
//   mongosh "<tenant DbConnectionString>" scripts/backfill-organization-name.js                       # dry run
//   mongosh "<tenant DbConnectionString>" --eval "const DRY_RUN=false" scripts/backfill-organization-name.js

const DB_NAME = "REPLACE_WITH_TENANT_DB_NAME";
const IAM_DB_NAME = DB_NAME; // IAM's Organizations live in the tenant database; change if not.
const DEFAULT_ORGANIZATION_ID = "default";
const DEFAULT_ORGANIZATION_NAME = "Default";

if (DB_NAME.startsWith("REPLACE")) throw new Error("Set DB_NAME to the tenant's DBName first.");
const dryRun = typeof DRY_RUN === "undefined" ? true : DRY_RUN;
const db = globalThis.db.getSiblingDB(DB_NAME);
const iam = globalThis.db.getSiblingDB(IAM_DB_NAME);

const missing = { $or: [{ OrganizationName: { $exists: false } }, { OrganizationName: null }, { OrganizationName: "" }] };
const orgIds = db.Subscriptions.distinct("OrganizationId", missing);

const names = new Map();
for (const id of orgIds) {
  if (id === DEFAULT_ORGANIZATION_ID) {
    names.set(id, DEFAULT_ORGANIZATION_NAME);
    continue;
  }
  const name = iam.Organizations.findOne({ _id: id }, { Name: 1 })?.Name?.trim();
  if (name) names.set(id, name);
}

print(`${DB_NAME}: ${db.Subscriptions.countDocuments(missing)} subscriptions across ${orgIds.length} organizations lack a name`);
for (const id of orgIds) {
  const count = db.Subscriptions.countDocuments({ ...missing, OrganizationId: id });
  print(`  ${id} -> ${names.has(id) ? JSON.stringify(names.get(id)) : "NOT FOUND in IAM, skipped"} (${count})`);
}

if (dryRun || names.size === 0) {
  print(dryRun ? "Dry run, nothing written." : "Nothing to do.");
} else {
  for (const [id, name] of names) {
    const r = db.Subscriptions.updateMany({ ...missing, OrganizationId: id }, { $set: { OrganizationName: name } });
    print(`  ${id}: matched ${r.matchedCount}, modified ${r.modifiedCount}`);
  }
}
