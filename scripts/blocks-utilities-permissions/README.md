# blocks-utilities permission seeder

Inserts the 30 endpoint permissions that
[`server/Api/Controllers`](../../server/Api/Controllers/README.md) guards its subscription and
payment endpoints with, into the `Permissions` collection of every tenant database.

Until these rows exist, every `[ProtectedEndPoint("blocks-utilities::...")]` endpoint answers 403.

## Run it

Dry run first — the script ships with `DRY_RUN = true`, so this writes nothing:

```bash
mongosh "mongodb://<user>:<password>@<host>:27017/?authSource=admin&socketTimeoutMS=0" --quiet --file seed-permissions.js
```

Read the per-database plan it prints. Then set `DRY_RUN = false` at the top of
`seed-permissions.js` and run the same command again.

To pilot on one tenant first, set `ONLY_DBS` near the top:

```js
const ONLY_DBS = new Set(["<some-tenant-db>"]);
```

## What it does

1. Loads `BlocksRootDb.Tenants` and maps `DBName` → tenant, exactly as the captcha finder does.
2. Takes every database that has a tenant row, skipping `admin`, `config`, `local` and the root db.
   A database with no `Permissions` collection is **skipped**, not created — a tenant without that
   collection is not using IAM permissions. Set `CREATE_COLLECTION_IF_MISSING = true` to change that.
3. For each database, copies `Permissions` to `Permissions_backup_<UTC timestamp>` and verifies the
   copy has the same document count. **A database whose backup does not verify is skipped entirely** —
   no inserts happen there.
4. Inserts only the resources not already present, matched on `Resource` + `OrganizationId`.

## What it will not do

- **It never updates, replaces or deletes anything.** The only write verbs in the file are
  `insertMany` and the `$out` that produces the backup. A resource that already exists is counted
  and skipped, never overwritten — including any permission created by hand in the Blocks OS UI.
- **It grants nobody anything.** Every permission is inserted with `Roles: []`. Assign them to roles
  in Blocks OS (Identity & Access → Roles) afterwards. Change `ROLES` at the top if you want a role
  attached at insert time.
- **It is re-runnable.** A second run inserts nothing.

## Rollback

Each run stamps one backup name, printed in the summary. To undo a run in a database, restore that
collection over `Permissions`:

```js
db.Permissions_backup_20260906_093701.aggregate([{ $out: "Permissions" }])
```

Or, to remove just what this run added without disturbing anything else:

```js
db.Permissions.deleteMany({ ResourceGroup: "blocks-utilities", CreatedBy: "seed-permissions.js" })
```

## Document shape

The Blocks OS *New Permission* form posts camelCase JSON, but that is the API contract — what lands
in Mongo is `Iam.DomainService.Entities.Permission : BuiltInPermission : Blocks.Genesis.BaseEntity`,
which is PascalCase, with `ItemId` carrying `[BsonId]` so it is stored as `_id`. Inserting camelCase
documents would leave them invisible to `ProtectedEndpointAccessHandler`, which queries `Resource`,
`OrganizationId` and `Roles`.

```json
{
  "_id": "2e142c74-4fc8-44ba-ab7f-3347492fbefa",
  "Name": "Read Payments",
  "Type": 1,
  "Description": "List and read payments.",
  "Resource": "blocks-utilities::payment::read",
  "ResourceGroup": "blocks-utilities",
  "IsBuiltIn": false,
  "IsArchived": false,
  "PermissionSeverity": 2,
  "DependentPermissions": [],
  "Roles": [],
  "OrganizationId": "default",
  "Tags": [],
  "Language": "en",
  "CreatedDate": "...",
  "LastUpdatedDate": "...",
  "CreatedBy": "seed-permissions.js",
  "LastUpdatedBy": "seed-permissions.js"
}
```

`Type` is `ResourceType`: `None=0, Endpoint=1, FrontendAction=2, DataProtection=3`.
`PermissionSeverity` is `None=0, Critical=1, High=2, Medium=3, Low=4` — severities assigned in the
script are a starting point, not a rule; edit the table if your tenants grade these differently.

`OrganizationId` is `"default"`, which is the value `ProtectedEndpointAccessHandler` falls back to
when the caller's context carries no organization — so these apply tenant-wide. Seed per-organization
copies by re-running with `ORGANIZATION_ID` set to that organization.
