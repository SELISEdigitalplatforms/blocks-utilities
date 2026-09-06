// =============================================================================
//  seed-permissions.js
//
//  Inserts the blocks-utilities endpoint permissions into the "Permissions"
//  collection of every tenant database.
//
//  Guarantees:
//    * Every Permissions collection is copied to a timestamped backup before
//      that database is touched. A database whose backup fails is skipped.
//    * Insert only. The script contains no update, replace or delete of any
//      kind - an existing permission is never modified, and a resource that
//      already exists is skipped rather than overwritten.
//    * Re-runnable. A second run inserts nothing.
//    * DRY_RUN is on by default: nothing is written until you turn it off.
//
//  Usage:
//    mongosh "<connection-string>" --quiet --file seed-permissions.js
// =============================================================================

(async () => {

// =============================================================================
// CONFIGURATION
//
// Every setting here can be supplied from outside instead of edited, so this
// file does not have to be changed to run it. run.ps1 / run.sh pass them:
//
//   mongosh "<uri>" --eval "SEED_APPLY=true" --file seed-permissions.js
//
// The values below are the defaults used when nothing is passed.
// =============================================================================

function setting(name, fallback) {
    return (typeof globalThis[name] !== "undefined" && globalThis[name] !== null)
        ? globalThis[name]
        : fallback;
}

// Nothing is written unless SEED_APPLY is true. Dry run is the default in every
// direction: forgetting the flag prints a plan, it does not write.
const DRY_RUN = setting("SEED_APPLY", false) !== true;

const ROOT_DB    = "BlocksRootDb";
const TENANT_COL = "Tenants";
const PERM_COL   = "Permissions";
const SKIP_DBS   = new Set(["admin", "config", "local", ROOT_DB]);

// Restrict the run to specific tenant databases. Empty means every tenant.
// Pass SEED_ONLY_DBS=["db-a","db-b"] to pilot.
const ONLY_DBS = new Set(setting("SEED_ONLY_DBS", []));

// A tenant database with no Permissions collection is not using IAM
// permissions. Left alone by default rather than having one created for it.
const CREATE_COLLECTION_IF_MISSING = false;

// BaseEntity.OrganizationId. The framework's ProtectedEndpointAccessHandler
// looks up permissions by the caller's OrganizationId, falling back to
// "default" when the caller's context carries none - so "default" is what makes
// a permission apply tenant-wide.
const ORGANIZATION_ID = setting("SEED_ORGANIZATION_ID", "default");

// Roles granted at insert time. Empty on purpose: granting is a decision for
// whoever owns the tenant's roles, and is made in Blocks OS afterwards. Every
// permission is inserted with no roles, so running this grants nobody anything.
const ROLES = setting("SEED_ROLES", []);

// Existing rows carry the Blocks OS user id of whoever created them. Set this to
// the operator's user id to attribute the seed; null leaves it unattributed,
// which is what the audit trail shows for anything not created by a person.
const CREATED_BY = setting("SEED_CREATED_BY", null);

// Existing rows carry null here, not a language code.
const LANGUAGE = null;

// Name is per database. {db} is the tenant database, {tenant} its display name,
// {name} the label from the table below, {resource} the full resource string.
const NAME_TEMPLATE = setting("SEED_NAME_TEMPLATE", "{db} - {name}");

// ResourceType: None=0, Endpoint=1, FrontendAction=2, DataProtection=3
const TYPE_ENDPOINT = 1;

// PermissionSeverity: None=0, Critical=1, High=2, Medium=3, Low=4
const CRITICAL = 1, HIGH = 2, MEDIUM = 3;

// =============================================================================
// THE PERMISSIONS
//
// Resource strings must match the [ProtectedEndPoint("...")] attributes in
// server/Api/Controllers exactly. See server/Api/Controllers/README.md.
// =============================================================================

const RESOURCE_GROUP = "blocks-utilities";

const PERMISSIONS = [
    // --- payments -----------------------------------------------------------
    ["blocks-utilities::payment::read",                         "Read Payments",                   HIGH,     "List and read payments."],
    ["blocks-utilities::payment::manage",                       "Manage Payments",                 HIGH,     "Create one-off and recurring payments."],
    ["blocks-utilities::payment::read-refund",                  "Read Payment Refunds",            HIGH,     "Read refunds raised against a payment."],
    ["blocks-utilities::payment::manage-refund",                "Manage Payment Refunds",          HIGH,     "Raise a refund against a payment."],
    ["blocks-utilities::payment::read-capture",                 "Read Payment Captures",           HIGH,     "Read captures taken against a payment."],
    ["blocks-utilities::payment::manage-capture",               "Manage Payment Captures",         HIGH,     "Capture an authorised payment."],

    // --- stored payment methods ---------------------------------------------
    ["blocks-utilities::payment-method::read",                  "Read Payment Methods",            HIGH,     "List a subscriber's stored payment methods."],
    ["blocks-utilities::payment-method::manage",                "Manage Payment Methods",          HIGH,     "Remove a stored payment method."],

    // --- payment providers --------------------------------------------------
    ["blocks-utilities::payment-provider::read",                "Read Payment Providers",          MEDIUM,   "Read configured payment providers."],
    ["blocks-utilities::payment-provider::manage",              "Manage Payment Providers",        CRITICAL, "Register, update and rotate payment provider credentials."],
    ["blocks-utilities::payment-provider::read-encryption",     "Read Provider Encryption Health", MEDIUM,   "Read the health of provider secret encryption."],
    ["blocks-utilities::payment-provider::manage-encryption",   "Re-encrypt Provider Secrets",     CRITICAL, "Re-encrypt stored payment provider secrets."],

    // --- subscriptions ------------------------------------------------------
    ["blocks-utilities::subscription::read",                    "Read Subscriptions",              HIGH,     "Read the current subscription, its audit trail and every preview."],
    ["blocks-utilities::subscription::manage",                  "Manage Subscriptions",            HIGH,     "Subscribe, cancel, change plan or quantity, set up a payment method."],
    ["blocks-utilities::subscription::read-invoice",            "Read Invoices",                   HIGH,     "Read invoice history and download invoice PDFs."],
    ["blocks-utilities::subscription::manage-invoice",          "Resend Invoices",                 HIGH,     "Resend an invoice to the subscriber."],

    // --- entitlements -------------------------------------------------------
    ["blocks-utilities::entitlement::read",                     "Read Entitlements",               MEDIUM,   "Read the entitlement snapshot and single entitlements."],

    // --- plan catalogue -----------------------------------------------------
    ["blocks-utilities::subscription-plan::read",               "Read Subscription Plans",         MEDIUM,   "Read the plan catalogue and individual plans."],
    ["blocks-utilities::subscription-plan::manage",             "Manage Subscription Plans",       HIGH,     "Create, update and archive plans and prices."],

    // --- discounts ----------------------------------------------------------
    ["blocks-utilities::subscription-discount::read",           "Read Subscription Discounts",     MEDIUM,   "List, read and preview discounts."],
    ["blocks-utilities::subscription-discount::manage",         "Manage Subscription Discounts",   HIGH,     "Create, update and archive discounts."],

    // --- metered usage ------------------------------------------------------
    ["blocks-utilities::subscription-usage::read",              "Read Subscription Usage",         MEDIUM,   "Read current usage and preview overage."],
    ["blocks-utilities::subscription-usage::manage",            "Record Subscription Usage",       HIGH,     "Record metered usage against a subscription."],

    // --- billing / merchant profile -----------------------------------------
    ["blocks-utilities::subscription-billing-profile::read",    "Read Billing Profile",            HIGH,     "Read the tenant's billing profile."],
    ["blocks-utilities::subscription-billing-profile::manage",  "Manage Billing Profile",          HIGH,     "Update the tenant's billing profile."],
    ["blocks-utilities::subscription-merchant-profile::read",   "Read Merchant Profile",           MEDIUM,   "Read the tenant's merchant profile."],
    ["blocks-utilities::subscription-merchant-profile::manage", "Manage Merchant Profile",         HIGH,     "Update the tenant's merchant profile."],

    // --- simulation harness -------------------------------------------------
    ["blocks-utilities::subscription-simulation::read",         "Read Subscription Simulation",    MEDIUM,   "Read simulation state and simulation data."],
    ["blocks-utilities::subscription-simulation::manage",       "Manage Subscription Simulation",  CRITICAL, "Rewrite billing state through the simulation harness."],

    // --- background work recovery -------------------------------------------
    ["blocks-utilities::subscription-background-work::manage",  "Manage Background Work",          CRITICAL, "List, requeue and abandon dead-lettered billing work."]
];

// =============================================================================
// HELPERS
// =============================================================================

function pad2(n) { return String(n).padStart(2, "0"); }

const now       = new Date();
const RUN_STAMP = `${now.getUTCFullYear()}${pad2(now.getUTCMonth() + 1)}${pad2(now.getUTCDate())}` +
                  `_${pad2(now.getUTCHours())}${pad2(now.getUTCMinutes())}${pad2(now.getUTCSeconds())}`;
const BACKUP_COL = `${PERM_COL}_backup_${RUN_STAMP}`;

const HEX = "0123456789abcdef";
function newGuid() {
    let s = "";
    for (let i = 0; i < 32; i++) {
        s += (i === 12) ? "4"
           : (i === 16) ? HEX[(Math.floor(Math.random() * 16) & 0x3) | 0x8]
           : HEX[Math.floor(Math.random() * 16)];
        if (i === 7 || i === 11 || i === 15 || i === 19) s += "-";
    }
    return s;
}

function buildName(name, resource, dbName, tenantName) {
    return NAME_TEMPLATE
        .replace("{db}", dbName)
        .replace("{tenant}", tenantName || dbName)
        .replace("{name}", name)
        .replace("{resource}", resource);
}

// The stored shape is Iam.DomainService.Entities.Permission : BuiltInPermission
// : Blocks.Genesis.BaseEntity - PascalCase, with ItemId carrying [BsonId] so it
// is written as _id. The camelCase JSON the Blocks OS form posts is the API
// contract, not what lands in Mongo. Field order matches an existing row so
// exports and diffs line up.
function buildPermission(resource, name, severity, description, dbName, tenantName) {
    return {
        _id:                  newGuid(),
        CreatedDate:          now,
        LastUpdatedDate:      now,
        CreatedBy:            CREATED_BY,
        Language:             LANGUAGE,
        LastUpdatedBy:        CREATED_BY,
        Tags:                 [],
        Name:                 buildName(name, resource, dbName, tenantName),
        Type:                 TYPE_ENDPOINT,
        PermissionSeverity:   severity,
        Description:          description,
        Resource:             resource,
        ResourceGroup:        RESOURCE_GROUP,
        IsBuiltIn:            false,
        IsArchived:           false,
        DependentPermissions: [],
        Roles:                ROLES.slice(),
        OrganizationId:       ORGANIZATION_ID
    };
}

// =============================================================================
// BANNER
// =============================================================================

print(``);
print(`==========================================`);
print(`  blocks-utilities Permission Seeder`);
print(`==========================================`);
print(``);
print(`  Mode            : ${DRY_RUN ? "DRY RUN - nothing will be written" : "APPLY - inserts will be committed"}`);
print(`  Permissions     : ${PERMISSIONS.length}`);
print(`  Resource group  : ${RESOURCE_GROUP}`);
print(`  OrganizationId  : ${ORGANIZATION_ID}`);
print(`  Roles granted   : ${ROLES.length === 0 ? "(none - assign in Blocks OS)" : ROLES.join(", ")}`);
print(`  Name format     : ${NAME_TEMPLATE}`);
print(`  Backup name     : ${BACKUP_COL}`);
print(``);

// Fail fast on a duplicate resource in the list above.
const seen = new Set();
for (const [resource] of PERMISSIONS) {
    if (seen.has(resource)) {
        print(`  ERROR: duplicate resource in PERMISSIONS: ${resource}`);
        quit(1);
    }
    seen.add(resource);
}

// =============================================================================
// STEP 1 - tenants
// =============================================================================

print(`  Loading tenants from ${ROOT_DB}.${TENANT_COL}...`);

const tenants = db.getSiblingDB(ROOT_DB).getCollection(TENANT_COL).find({}).toArray();

if (tenants.length === 0) {
    print(`  ERROR: No tenants found.`);
    quit(1);
}

const dbToTenant = {};
for (const t of tenants) {
    const dbName = t.DBName;
    if (dbName) {
        dbToTenant[dbName] = { tenantId: t.TenantId ?? t._id, name: t.Name ?? "" };
    }
}

print(`  Tenants loaded      : ${tenants.length}`);
print(`  Tenants with DBName : ${Object.keys(dbToTenant).length}`);

if (Object.keys(dbToTenant).length === 0) {
    print(`  !! No DBName on any tenant. Sample document:`);
    print(JSON.stringify(tenants[0], null, 2));
    quit(1);
}

// =============================================================================
// STEP 2 - target databases
// =============================================================================

const allDbs = db.adminCommand({ listDatabases: 1 })
    .databases
    .map(d => d.name)
    .filter(name => !SKIP_DBS.has(name))
    .filter(name => dbToTenant[name])
    .filter(name => ONLY_DBS.size === 0 || ONLY_DBS.has(name));

print(`  Target databases    : ${allDbs.length}${ONLY_DBS.size > 0 ? "  (restricted by ONLY_DBS)" : ""}`);
print(``);

if (allDbs.length === 0) {
    print(`  Nothing to do.`);
    quit(0);
}

// =============================================================================
// STEP 3 - per database: back up, then insert what is missing
// =============================================================================

const report      = [];
let   totalInsert = 0;
let   totalSkip   = 0;
let   errorCount  = 0;

for (const dbName of allDbs) {
    const target = db.getSiblingDB(dbName);
    const line   = { dbName, tenant: dbToTenant[dbName].name, inserted: 0, existing: 0, status: "" };

    try {
        const hasCol = target.getCollectionNames().includes(PERM_COL);

        if (!hasCol && !CREATE_COLLECTION_IF_MISSING) {
            line.status = "skipped - no Permissions collection";
            report.push(line);
            continue;
        }

        // --- which of our resources are already there -----------------------
        const existing = hasCol
            ? target.getCollection(PERM_COL)
                  .find({ Resource: { $in: PERMISSIONS.map(p => p[0]) },
                          OrganizationId: ORGANIZATION_ID })
                  .toArray()
                  .map(d => d.Resource)
            : [];

        const existingSet = new Set(existing);
        const missing     = PERMISSIONS.filter(p => !existingSet.has(p[0]));

        line.existing = existingSet.size;

        if (missing.length === 0) {
            line.status = "up to date";
            report.push(line);
            totalSkip += existingSet.size;
            continue;
        }

        if (DRY_RUN) {
            line.inserted = missing.length;
            line.status   = "would insert";
            report.push(line);
            totalInsert += missing.length;
            totalSkip   += existingSet.size;
            continue;
        }

        // --- backup, before anything is written -----------------------------
        // A database whose backup does not verify is left untouched.
        const docCount = hasCol ? target.getCollection(PERM_COL).countDocuments({}) : 0;

        if (docCount > 0) {
            target.getCollection(PERM_COL).aggregate([{ $out: BACKUP_COL }]).toArray();

            const backedUp = target.getCollection(BACKUP_COL).countDocuments({});
            if (backedUp !== docCount) {
                line.status = `ABORTED - backup mismatch (${backedUp}/${docCount})`;
                report.push(line);
                errorCount++;
                continue;
            }
        }

        // --- insert ---------------------------------------------------------
        const docs   = missing.map(([resource, name, severity, description]) =>
                           buildPermission(resource, name, severity, description,
                                           dbName, dbToTenant[dbName].name));
        const result = target.getCollection(PERM_COL).insertMany(docs, { ordered: false });

        line.inserted = Object.keys(result.insertedIds).length;
        line.status   = docCount > 0 ? `inserted (backup: ${BACKUP_COL})` : "inserted (collection was empty)";

        totalInsert += line.inserted;
        totalSkip   += existingSet.size;
        report.push(line);

    } catch (err) {
        line.status = `ERROR - ${err.message}`;
        report.push(line);
        errorCount++;
    }
}

// =============================================================================
// RESULTS
// =============================================================================

print(`==========================================`);
print(`  Result`);
print(`==========================================`);
print(``);
print(`  ${"DBName".padEnd(34)} ${"Tenant".padEnd(24)} ${"New".padStart(4)} ${"Have".padStart(5)}  Status`);
print(`  ${"-".repeat(34)} ${"-".repeat(24)} ${"-".repeat(4)} ${"-".repeat(5)}  ${"-".repeat(40)}`);

for (const r of report) {
    print(`  ${String(r.dbName).padEnd(34)} ${String(r.tenant).substring(0, 24).padEnd(24)} ` +
          `${String(r.inserted).padStart(4)} ${String(r.existing).padStart(5)}  ${r.status}`);
}

print(``);
print(`==========================================`);
print(`  Databases processed : ${report.length}`);
print(`  Permissions ${DRY_RUN ? "to insert" : "inserted "} : ${totalInsert}`);
print(`  Already present     : ${totalSkip}`);
print(`  Errors              : ${errorCount}`);
print(`==========================================`);

if (DRY_RUN) {
    const firstDb = allDbs[0];
    const [r, n, s, d] = PERMISSIONS[0];
    print(``);
    print(`  Sample document (for ${firstDb}):`);
    print(JSON.stringify(
        buildPermission(r, n, s, d, firstDb, dbToTenant[firstDb].name), null, 2)
        .split("\n").map(l => `    ${l}`).join("\n"));
    print(``);
    print(`  DRY RUN - nothing was written.`);
    print(`  Re-run with the apply switch to commit these inserts.`);
} else {
    print(``);
    print(`  Backups written to "${BACKUP_COL}" in every database that had`);
    print(`  existing permissions. To roll this run back, restore that`);
    print(`  collection over "${PERM_COL}".`);
}

print(`==========================================`);
print(``);

})();
