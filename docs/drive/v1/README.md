# OneDrive and SharePoint — the complete flow (v1)

Written for the engineer **integrating the SELISE Signature app** with a user's OneDrive or
SharePoint, and for the operator who has to set the feature up or find out why a connection failed.

Everything happens over HTTP against the Utilities API. Nothing in the console shows it. The
feature does two jobs:

1. **Pick** a file from the user's drive and copy it into Blocks Storage, so the Signature app can
   work on it like any uploaded file.
2. **Save back** files from Blocks Storage (the signed archive) into a folder the user chooses.

| Part | Read it when |
| --- | --- |
| [How it fits together](#how-it-fits-together) | You want the model before the endpoints. |
| [Setting it up](#setting-it-up) | The feature answers `drive_not_configured`, or a new environment is being stood up. |
| [Connecting a user](#connecting-a-user) | You are wiring the "Connect OneDrive" button. |
| [Browsing](#browsing) | You are building the picker. |
| [Importing](#importing) | The user chose a file. |
| [Saving back](#saving-back) | The signed archive has to land in the user's drive. |
| [Errors](#errors) | A call failed and you need to know what to tell the user. |
| [Testing by hand](#testing-by-hand) | You want to prove an environment works with Postman or curl. |
| [Tracing](#tracing) | A user reports a problem. |

The implementation authority is the Drive module under
[`server/Utility.DomainService/Drive/`](../../../server/Utility.DomainService/Drive/) and the
approved spec [`docs/specs/004-drive-integration/README.md`](../../specs/004-drive-integration/README.md).
Where this page and the code disagree, the code is right and this page is a bug.

## How it fits together

```
Signature app (browser)            Utilities API                      Microsoft
─────────────────────────          ─────────────────────────          ────────────────────
POST api/drive/connections  ─────► store state + PKCE verifier
◄──── authorizeUrl
open authorizeUrl ──────────────────────────────────────────────────► Entra sign-in + consent
                                                                      │
◄──────────────────────────── GET drive/connections/callback ◄────────┘ code + state
                                   exchange code, encrypt refresh
                                   token, store connection
◄──── 302 returnUrl?driveStatus=connected

GET api/drive/items ────────► refresh access token (cached) ─────────► Graph /drives/{id}/...
◄──── items page
POST api/drive/imports ─────► Graph download ──► Blocks Storage upload
◄──── fileId
POST api/drive/uploads ─────► Blocks Storage download ──► Graph upload session
◄──── per-file results
```

Four facts shape every call:

- **One Entra app for the whole platform.** It is multi-tenant, so a user from any Microsoft
  organization, or a personal account, can consent to it. The backend only ever sees what that user
  could open themselves.
- **One connection per user per project.** The caller is identified by the bearer token (user) and
  `x-blocks-key` (project). No request can name another user. Connecting again replaces the
  connection; disconnecting deletes it.
- **OneDrive and SharePoint are the same model.** Graph exposes both as drives of items. A request
  says `source: onedrive` for the user's own drive or `source: sharepoint` plus a `siteId` for a
  site's document library. After that the item ids and drive ids are interchangeable.
- **The refresh token is the only secret stored**, AES-GCM encrypted with a key from Blocks
  Secrets. Access tokens live in memory for their lifetime and are refreshed on demand.

## Setting it up

The API needs an Entra app registration and two Blocks Secrets in each project that uses the
feature. Without them, every Drive call except `GET connections/me` answers
`503 drive_not_configured`.

### Entra app registration

One registration, owned by SELISE, reused by every environment.

| Setting | Value |
| --- | --- |
| Supported account types | Accounts in any organizational directory and personal Microsoft accounts (multi-tenant) |
| Platform | Web |
| Redirect URIs | `https://<utilities host>/drive/connections/callback` for every environment (no `api` segment; see [why](#why-the-callback-is-outside-api)) |
| Delegated permissions | `Files.ReadWrite.All`, `Sites.ReadWrite.All`, `User.Read`, `offline_access`, `openid`, `profile`, `email` |
| Client secret | Stored in Blocks Secrets, never in configuration |

### Configuration (`Drive` section of the Api `appsettings`)

| Key | Meaning | Default |
| --- | --- | --- |
| `ClientId` | The Entra application (client) id. | — |
| `ClientSecretSecretId` | Blocks Secrets id holding the Entra client secret. | — |
| `TokenEncryptionKeySecretId` | Blocks Secrets id holding the 32-byte base64 AES key for refresh tokens. | — |
| `RedirectUrl` | The callback URL registered on the Entra app, per environment. | — |
| `TokenEncryptionKeyId` | Label stored beside each ciphertext so keys can be rotated later. | `drive-v1` |
| `SecretCacheSeconds` | How long a resolved secret is reused before Blocks Secrets is asked again. | `300` |
| `Scopes` | Space-separated scopes requested at sign-in. | the delegated permissions above |
| `MaxFileSizeBytes` | Largest file imported or uploaded. | 250 MiB |
| `SimpleUploadLimitBytes` | Files up to this size go to Graph in one PUT; larger ones use an upload session. | 4 MiB |
| `UploadChunkSizeBytes` | Chunk size for upload sessions (must be a multiple of 320 KiB). | 10 MiB |
| `MaxUploadFilesPerRequest` | Cap on `fileIds` in one `POST uploads`. | `20` |
| `StateTtlMinutes` | How long an `authorizeUrl` stays usable. | `10` |
| `MaxRetries`, `DefaultRetryAfterSeconds` | Retry budget when Graph throttles (429/503). | `3`, `2` |
| `DefaultPageSize`, `MaxPageSize` | Listing page size when omitted, and the cap. | `50`, `200` |
| `AllowLocalhostReturnUrl` | Accept `http://localhost` return URLs. Development only. | `false` |
| `ImportDirectory` | Storage directory that receives imported files. | `Blocks-Utilities-Drive-Imports` |

The two secret ids point into **Blocks Secrets of the calling project**. A secret created in one
project is not visible from another, so each project that uses the feature carries both secrets.
Resolved values are cached per project for `SecretCacheSeconds`.

### The project's domains

`returnUrl` on `POST connections` must be an https URL whose host is one of the project's
registered **application domains** in Blocks Cloud (the tenant's `Applications[].Domain`). The
match is exact on the host. This is what stops the anonymous callback from becoming an open
redirect. For the Signature app this is automatic: its own page URL is on its own project's domain.
For a manual test, pick any https URL on a domain the project already has, or add one in Blocks
Cloud; the lookup is live.

### Why the callback is outside `api`

Genesis's tenant-validation middleware guards every path under `api/` and refuses any request whose
`Origin` or `Referer` is not one of the project's domains (`406 Invalid_Origin_Or_Referer`). Entra
redirects the browser back with a `login.microsoftonline.com` referer. The callback therefore lives
at `/drive/connections/callback`, outside the prefix, like the payment and SMS webhooks. It is
anonymous, trusts nothing from the query but `code` and `state`, and only ever redirects to the
`returnUrl` stored with that state.

## Connecting a user

All endpoints below are under `https://<utilities host>/api/drive` and need two headers:

```
Authorization: Bearer <Blocks access token of the user>
x-blocks-key: <project key>
```

### 1. Is the user connected?

`GET connections/me` → `200`

```json
{
  "connected": false,
  "accountDisplayName": null,
  "accountEmail": null,
  "connectedAt": null,
  "adminConsentUrl": "https://login.microsoftonline.com/organizations/v2.0/adminconsent?client_id=...&scope=...&redirect_uri=..."
}
```

`adminConsentUrl` is always present so that a client told `admin_consent_required` has something to
show the user's IT admin without a second call. It is **not** the user's sign-in link.

### 2. Start

`POST connections` with the page the browser should return to:

```json
{ "returnUrl": "https://sign.example.com/documents/123" }
```

| Status | Body | Meaning |
| --- | --- | --- |
| `200` | `{ "authorizeUrl": "https://login.microsoftonline.com/common/oauth2/v2.0/authorize?...", "connected": false }` | Send the browser to `authorizeUrl`. |
| `200` | `{ "authorizeUrl": null, "connected": true }` | Already connected; nothing to do. |
| `400 drive_invalid_request` | | `returnUrl` missing, not https, or not on a project domain. |
| `503 drive_not_configured` | | Client id, secret or encryption key missing. |

The `authorizeUrl` carries a random `state` and a PKCE challenge. The state is **single use and
expires after ten minutes**, so open the URL promptly and do not reuse it. It is bound to the user
whose bearer made the call.

### 3. The user signs in

Open `authorizeUrl` in the same tab or a popup. The user signs in to Microsoft and consents. If an
IT admin has already granted admin consent for their organization, no consent screen is shown.

### 4. The callback

Entra redirects to `GET /drive/connections/callback?code=...&state=...`. The API exchanges the
code, reads the account's display name, email and Microsoft tenant id from the id token, encrypts
the refresh token, stores the connection, and redirects the browser to the stored `returnUrl` with
a `driveStatus` query:

| Landing URL | Meaning |
| --- | --- |
| `returnUrl?driveStatus=connected` | Done. `GET connections/me` now reports the account. |
| `returnUrl?driveStatus=failed&reason=<reason>` | Consent refused or Entra failed. `reason` is one of `access_denied`, `invalid_grant`, `invalid_request`, `invalid_client`, `unauthorized_client`, `server_error`, `temporarily_unavailable`, `not_configured`, `invalid_response`, `unreachable`, `provider_error`. Anything Entra sent outside that set is logged, never echoed. |
| `returnUrl?driveStatus=admin_consent_required` | The organization lets only admins consent (AADSTS65001). Show `adminConsentUrl` to the admin, then let the user try again. |
| `400 Unknown or expired state.` | No redirect, because there is no trusted place to send the browser. The state was replayed, expired, or forged. |

If `returnUrl` already has a query string the parameters are appended with `&`.

### Admin consent

`adminConsentUrl` opens Entra's admin-consent page. An admin signs in, approves the app for the
whole organization, and Entra sends them to the same callback with `admin_consent=True&tenant=...`.
The callback answers a plain-text page ("Admin consent recorded...") and touches no state, because
the admin is in a separate browser, not the user's flow. Users of that organization then connect
through steps 2 to 4 without a consent prompt.

### Disconnect

`DELETE connections/me` → `204` when a connection was removed, `404 drive_not_connected` when
there was none. The refresh token is deleted and the cached access token discarded. Microsoft is
not told; the user can revoke the app under their Microsoft account's app permissions.

### Token lifetime

The stored refresh token is used whenever a Drive call needs an access token, and Entra rotates
it on each use; the new one replaces the old. If Entra answers `invalid_grant` (password change,
revocation, long inactivity), the connection is deleted and the call answers
`401 drive_not_connected`: send the user through step 2 again. `LastUsedAt` on the connection
moves on every successful refresh.

## Browsing

Every listing or write names a drive. Either describe it by source:

| Source | Fields | Resolves to |
| --- | --- | --- |
| `onedrive` | — | the user's own drive (`/me/drive`) |
| `sharepoint` | `siteId` | the site's default document library (`/sites/{siteId}/drive`) |

or, once you have one from an earlier answer, pass `driveId` directly and skip `source`.

### Sites

`GET sites?search=<text>` → `200` with a list of

```json
{ "siteId": "contoso.sharepoint.com,guid,guid", "name": "Legal", "webUrl": "https://contoso.sharepoint.com/sites/legal" }
```

With `search` empty or omitted the sites the user follows come back (`/me/followedSites`); a search runs across every site the user can reach. Site ids contain
commas; pass them through as-is.

### Items

`GET items` with query parameters:

| Parameter | Meaning |
| --- | --- |
| `source`, `siteId`, `driveId` | Which drive, as above. |
| `parentId` | Folder item id. Omit for the drive root. |
| `search` | Text query over names and content, drive-wide. |
| `sortBy` | `name`, `lastModifiedDateTime` or `size`. |
| `sortOrder` | `asc` (default) or `desc`. |
| `pageSize` | 1 to `MaxPageSize` (200). Default 50. |
| `cursor` | The `nextCursor` of the previous page. **Send it alone**; it already encodes drive, folder and search. |

Answer, `200`:

```json
{
  "items": [
    {
      "driveId": "b!abc...",
      "itemId": "01ABC...",
      "name": "contract.pdf",
      "kind": "file",
      "size": 48213,
      "mimeType": "application/pdf",
      "createdAt": "2026-10-01T09:12:00Z",
      "modifiedAt": "2026-10-20T15:03:11Z",
      "webUrl": "https://...",
      "parentId": "01ROOT..."
    }
  ],
  "nextCursor": "aHR0cHM6Ly9ncmFwaC5...",
  "sortApplied": true
}
```

`kind` is `file` or `folder`. Folders have `size` 0 and no `mimeType`. Paging is forward-only:
`nextCursor` is null on the last page. `sortApplied` is false when `search` was given, because
Graph cannot sort search results; show them in the order received.

A cursor is a base64url-encoded Graph "next link" and is checked to point at the Graph host before
it is followed, so a cursor from anywhere else is rejected with `400`.

Failures: `400 drive_invalid_request` (bad source, sort field, page size), `404 drive_item_not_found`
(folder or site gone), `429 drive_throttled` with `Retry-After`, `502 drive_provider_error`.

### Folders

`POST folders` → `201` with the new folder as a `DriveItemSummary`:

```json
{ "source": "onedrive", "parentId": "01ROOT...", "name": "Signed 2026" }
```

`parentId` omitted means the drive root. Names follow Graph's rules: 1 to 255 characters, none of
`" * : < > ? / \ |`, not `.` or `..`, no trailing dot. A folder of that name already in the parent
answers `409 drive_name_conflict`; folders are never renamed silently.

## Importing

`POST imports` copies one drive file into Blocks Storage.

```json
{ "driveId": "b!abc...", "itemId": "01ABC..." }
```

| Status | Meaning |
| --- | --- |
| `201` | `{ "fileId": "<storage file id>", "name": "contract.pdf", "size": 48213, "mimeType": "application/pdf" }` |
| `404 drive_item_not_found` | The item no longer exists or is a folder. |
| `413 drive_file_too_large` | Above `MaxFileSizeBytes`. |
| `502 drive_provider_error` | Graph refused or the download failed. |
| `500 drive_storage_error` | Blocks Storage did not accept the file. |

The file is streamed from Graph's short-lived download URL straight into a pre-signed Storage
upload, so nothing is buffered on disk. It lands in the project's `Blocks-Utilities-Drive-Imports`
directory with the drive's file name and MIME type, owned by the calling user. From here on it is
an ordinary Storage file and the Signature app treats the returned `fileId` like any upload.

## Saving back

`POST uploads` pushes Blocks Storage files into a drive folder.

```json
{ "source": "onedrive", "parentId": "01FOLDER...", "fileIds": ["<storage file id>", "..."] }
```

Up to `MaxUploadFilesPerRequest` (20) ids per call. The answer is `200` even when some files
failed, with one result per id in the order given:

```json
{
  "results": [
    { "fileId": "a1", "success": true,  "item": { "itemId": "01NEW...", "name": "contract-signed.zip", "kind": "file", "size": 120394, "...": "..." }, "error": null },
    { "fileId": "a2", "success": false, "item": null, "error": "ItemNotFound: ..." }
  ]
}
```

Behaviour worth knowing:

- The file keeps its Storage name. A clash in the target folder makes Graph **rename** the new
  file (`report.zip` → `report 1.zip`) rather than overwrite; the returned `item.name` is the
  final name.
- Files up to 4 MiB go in one request. Larger ones use a Graph upload session in 10 MiB chunks,
  which also lets a flaky chunk be retried without restarting.
- `413` per file above `MaxFileSizeBytes`; a Storage read failure is reported on that file only.
- Whole-request failures are the usual ones: `400` for a bad target or empty `fileIds`, `401` when
  not connected, `429` when Graph throttles before the first file.

## Errors

Every failure uses the standard envelope:

```json
{ "success": false, "data": null, "error": { "code": "drive_not_connected", "message": "...", "fields": null, "traceId": "..." }, "meta": { "correlationId": "...", "timestampUtc": "...", "replayed": false } }
```

| Status | Code | What to do |
| --- | --- | --- |
| `400` | `drive_invalid_request` | Fix the request; `message` says which field. |
| `401` | `drive_not_connected` | No connection, or the refresh token died. Run the connect flow. |
| `403` | `drive_admin_consent_required` | Show `adminConsentUrl` from `GET connections/me` to the IT admin. |
| `404` | `drive_item_not_found` | The item, folder or site is gone. Refresh the listing. |
| `409` | `drive_name_conflict` | Folder already exists. Pick another name or list and reuse it. |
| `413` | `drive_file_too_large` | Over the configured limit. |
| `429` | `drive_throttled` | Graph is throttling this user. Wait `Retry-After` seconds (header and message). The API already retried up to `MaxRetries` times before giving up. |
| `502` | `drive_provider_error` | Graph or Entra answered with an error. Usually transient. |
| `500` | `drive_storage_error` | Blocks Storage failed. Not a drive problem. |
| `503` | `drive_not_configured` | Missing client id, secret, encryption key or redirect URL. Operator work, see [Setting it up](#setting-it-up). |

A plain `401` without an envelope means the bearer or `x-blocks-key` was rejected before the Drive
code ran.

## Testing by hand

Set `token` (a Blocks access token for a user of the project) and `key` (the project's
`x-blocks-key`). `BASE` is the Utilities host, for example `https://utilities.seliseblocks.com`.

```bash
H=(-H "Authorization: Bearer $token" -H "x-blocks-key: $key" -H "Content-Type: application/json")

# status: expect connected:false
curl -s "$BASE/api/drive/connections/me" "${H[@]}"

# start: copy data.authorizeUrl, open it in a browser, sign in
curl -s -X POST "$BASE/api/drive/connections" "${H[@]}" \
  -d '{"returnUrl":"https://<a domain of the project>/drive-done"}'

# the browser lands on returnUrl?driveStatus=connected; status now shows the account
curl -s "$BASE/api/drive/connections/me" "${H[@]}"

# browse, search, page
curl -s "$BASE/api/drive/items?source=onedrive&pageSize=10&sortBy=name" "${H[@]}"
curl -s "$BASE/api/drive/items?source=onedrive&search=pdf" "${H[@]}"
curl -s "$BASE/api/drive/items?cursor=<nextCursor>" "${H[@]}"

# SharePoint
curl -s "$BASE/api/drive/sites" "${H[@]}"
curl -s "$BASE/api/drive/items?source=sharepoint&siteId=<siteId>" "${H[@]}"

# import a PDF into Blocks Storage (201, note fileId)
curl -s -X POST "$BASE/api/drive/imports" "${H[@]}" -d '{"driveId":"<driveId>","itemId":"<itemId>"}'

# folder (201, note itemId), then upload the stored file into it
curl -s -X POST "$BASE/api/drive/folders" "${H[@]}" -d '{"source":"onedrive","name":"Signed QA"}'
curl -s -X POST "$BASE/api/drive/uploads" "${H[@]}" \
  -d '{"source":"onedrive","parentId":"<folderItemId>","fileIds":["<fileId>"]}'

# disconnect
curl -s -X DELETE "$BASE/api/drive/connections/me" "${H[@]}"
```

What proves the environment is healthy:

- The imported file is visible in the project's Storage under `Blocks-Utilities-Drive-Imports`.
- The uploaded file is in the drive folder with the same bytes; a second upload of the same id
  produces a renamed copy, not an error.
- `POST folders` with the same name twice answers `409`.
- A `returnUrl` on a foreign host answers `400`.
- Opening an `authorizeUrl` twice: the second attempt lands on `400 Unknown or expired state.`

## Tracing

- **Connections** live in the root tenant database, collection `DriveConnections`, one row per
  (`ProjectKey`, `UserId`): account name and email, Microsoft tenant id, granted scopes,
  `ConnectedAt`, `LastUsedAt`, and the ciphertext. The token itself is never readable from the row.
- **Pending sign-ins** are in `DriveOAuthStates`, keyed by state, deleted on use and expired by a
  TTL index after `StateTtlMinutes`.
- **Logs** carry the `correlationId` from the response envelope. Entra and Graph error bodies are
  logged in full on the server and never echoed to the client.
- **Metrics** (meter `Blocks.Utility.Drive`): `drive.connections.created`,
  `drive.connections.revoked`, `drive.graph.throttled`, `drive.imports.bytes`,
  `drive.uploads.bytes`, and the `drive.graph.request.duration` histogram tagged by operation.
