# Spec 004: OneDrive / SharePoint Drive Integration

**Status:** Approved (2026-10-26), amended 2026-10-26: secrets read from Blocks Secrets by id at runtime, tenant domain fields confirmed
**Date:** 2026-10-26
**Reference implementation studied:** `l3-net-generic-dms/src/DriveIntegrations` (origin/main @ c562910)

## Problem

The SELISE Signature app lets a user upload a PDF to sign from their PC, OneDrive, or SharePoint, and
after signing lets them save the signed zip back to OneDrive or SharePoint. Today that capability
exists only inside the generic DMS repository, tied to its CQRS handlers, an in-memory OAuth state
store, plaintext refresh tokens, and whole-file memory buffering. Blocks Utilities has no drive
capability, so Signature cannot reach a drive through the platform.

Success: Signature calls Utilities endpoints only. A user connects their Microsoft account once per
project, then Signature can list, search, and browse the user's OneDrive and SharePoint sites, import
a chosen file into Blocks Storage, create a folder, and push one or more Blocks Storage files into a
chosen drive folder. No console UI is involved.

## Scope

### In

- One **platform-owned, multi-tenant** Microsoft Entra app registration. Users from any Microsoft
  organization or personal account consent to it with delegated permissions.
- **One connection per Blocks user per project** (`userId` + `projectKey`), covering both OneDrive
  and SharePoint with a single consent.
- Connect (start OAuth), OAuth callback, connection status, disconnect.
- Admin-consent surfacing: when Entra answers `AADSTS65001`, the callback reports
  `admin_consent_required` and status exposes an admin-consent URL.
- Sources: the user's OneDrive (`/me/drive`) and SharePoint sites (`/sites/{id}/drive`).
- Site discovery: search by text; empty text returns the user's followed sites.
- Browse a folder's children, search within a drive or folder, sort, forward-cursor paging.
- Create folder.
- Import a drive item into Blocks Storage; returns the new storage `fileId`.
- Upload one or more Blocks Storage files to a drive folder, conflict behavior `rename`,
  per-file results.
- Streaming for import and upload, Graph upload sessions above 4 MB, size cap 250 MB (configurable).
- Refresh token encrypted at rest with AES-GCM under a key held in Blocks Secrets and named by
  `Drive:TokenEncryptionKeySecretId`; access token held in memory only.
- Bounded retry on Graph `429`/`503` honoring `Retry-After`, max 3, then a `DRIVE_THROTTLED` result.
- OpenTelemetry metrics and Genesis logging.
- Backend only: new `Utility.DomainService/Drive/` capability, one Api controller, tests.

### Out

- Any console UI or client code.
- Rename, delete, move, copy, direct browser (multipart) upload.
- Sharing links, permissions, versions, recycle bin, shared-with-me.
- Admin acting on behalf of another user; listing other users' connections.
- Tracking whether an organization admin has granted consent.
- Message-bus events or audit records.
- Sovereign clouds (Graph China / US Gov) beyond leaving base URLs in appsettings.
- The DMS `OneDriveHandler` e-signature file-handler flow.

## Domain Model

Collections live in the root tenant database like MagicLink, isolated by `ProjectKey`.

### `DriveConnections`

| Field | Type | Notes |
|---|---|---|
| `ItemId` | string (BsonId) | GUID |
| `ProjectKey` | string | from `x-blocks-key` |
| `UserId` | string | from the bearer token |
| `RefreshTokenCiphertext` | string | AES-GCM, nonce \| tag \| ciphertext, base64 |
| `EncryptionKeyId` | string | `"drive-v1"`; reserved for rotation |
| `MicrosoftTenantId` | string | `tid` claim of the id token, for diagnostics |
| `AccountDisplayName` | string | `name` claim, returned by status |
| `AccountEmail` | string? | `preferred_username` claim |
| `Scopes` | string | granted scope string from the token response |
| `ConnectedAt` | DateTime (UTC) | |
| `LastUsedAt` | DateTime (UTC) | updated on each successful token refresh |

Unique index on (`ProjectKey`, `UserId`). Lifecycle: created on successful callback (replacing any
existing row for the same key), deleted on disconnect or when Entra rejects the refresh token. No
soft-delete, no history.

### `DriveOAuthStates`

| Field | Type | Notes |
|---|---|---|
| `ItemId` | string (BsonId) | the `state` value, 32 random bytes base64url |
| `ProjectKey`, `UserId` | string | |
| `ReturnUrl` | string | already validated |
| `CodeVerifier` | string | PKCE S256 verifier |
| `CreatedAt` | DateTime | TTL index, expires after 10 minutes |

Single use: deleted on first callback regardless of outcome.

### Value objects (not persisted)

- `DriveItemSummary`: `driveId`, `itemId`, `name`, `kind` (`file` \| `folder`), `size`,
  `mimeType?`, `createdAt`, `modifiedAt`, `webUrl`, `parentId?`.
- `SiteSummary`: `siteId`, `name`, `webUrl`. No `driveId`: Graph's site search cannot expand the
  drive, so the listing resolves it from `siteId` on first use instead of paying one call per site.
- `DriveLocator`: either `{ source: "onedrive" }` or `{ source: "sharepoint", siteId }`, plus
  optional `driveId`/`itemId`. OneDrive and SharePoint share one Graph `driveItem` model; the locator
  is the only place they differ.

## API Contract

Base route `api/drive`. All endpoints except the callback require `[Authorize]` and `x-blocks-key`.
`userId` and `projectKey` always come from the token and header, never from the body. Payloads are
wrapped in `ApiResponse<T>`; every status is declared with `[ProducesResponseType]`. Operation
results map through a `DriveApiResults` extension in `Api/Utilities/`, following
`GeolocationApiResults`.

| Verb | Route | Request | Success | Errors |
|---|---|---|---|---|
| POST | `connections` | `{ returnUrl }` | 200 `{ authorizeUrl }` or `{ connected: true }` if already connected | 400 invalid/unallowed returnUrl, 503 Entra config missing |
| GET | `connections/callback` | query `code`, `state`, `error`, `error_description` (anonymous) | 302 to `returnUrl?driveStatus=connected` | 302 `driveStatus=failed&reason=...`, 302 `driveStatus=admin_consent_required`, 400 unknown/expired state |
| GET | `connections/me` | | 200 `{ connected, accountDisplayName?, accountEmail?, connectedAt?, adminConsentUrl }` | |
| DELETE | `connections/me` | | 204 | 404 not connected |
| GET | `sites` | `search?` (empty = followed sites) | 200 `[SiteSummary]` | 401-equivalent `DRIVE_NOT_CONNECTED`, 429 `DRIVE_THROTTLED` |
| GET | `items` | `source`, `siteId?`, `driveId?`, `parentId?` (absent = root), `search?`, `sortBy?` (`name` \| `lastModifiedDateTime` \| `size`), `sortOrder?` (`asc` \| `desc`), `pageSize?` (1..200, default 50), `cursor?` | 200 `{ items: [DriveItemSummary], nextCursor? }` | `DRIVE_NOT_CONNECTED`, 404 `DRIVE_ITEM_NOT_FOUND`, 400 bad sort/pageSize/cursor, `DRIVE_THROTTLED` |
| POST | `folders` | `{ source, siteId?, driveId?, parentId?, name }` | 201 `DriveItemSummary` | 409 `DRIVE_NAME_CONFLICT`, 400 invalid name, `DRIVE_NOT_CONNECTED` |
| POST | `imports` | `{ driveId, itemId }` | 201 `{ fileId, name, size, mimeType }` | 404, 413 `DRIVE_FILE_TOO_LARGE`, `DRIVE_NOT_CONNECTED` |
| POST | `uploads` | `{ source, siteId?, driveId?, parentId?, fileIds: [..] }` (1..20) | 200 `{ results: [{ fileId, success, item?: DriveItemSummary, error? }] }` | 400 empty list, `DRIVE_NOT_CONNECTED` |

Error result codes (`DriveFailureKind`): `NotConnected`, `AdminConsentRequired`, `Throttled`
(carries `retryAfterSeconds`), `ItemNotFound`, `NameConflict`, `FileTooLarge`, `InvalidRequest`,
`ProviderError` (Graph 5xx after retries), `StorageError` (Blocks Storage failed).

Cursor: the Graph `@odata.nextLink`, base64url-encoded. The server rejects a cursor whose host is
not the configured Graph base URL.

Sorting with a `search` is not supported by Graph; `sortBy` is ignored when `search` is present
and the response says so via a `sortApplied: false` flag.

## Authorization

- Caller must be an authenticated Blocks user of the project identified by `x-blocks-key`.
- A caller can only ever see or act on their own connection. There is no on-behalf-of path.
- The callback is anonymous because Entra calls it. It trusts nothing from the query except `code`
  and `state`; project, user, and returnUrl are read from the stored state.
- `returnUrl` is accepted only if absolute `https` (or `http://localhost` in Development) and its
  host matches one of the project's registered domains as exposed by Genesis tenant data. See
  Deferred Decisions for the exact field.
- Microsoft scopes requested: `Files.ReadWrite.All Sites.ReadWrite.All offline_access User.Read
  openid profile email`. Fixed in appsettings, not per project.

## Edge Cases & Failure Modes

- **Already connected** → `POST connections` returns `{ connected: true }` and no URL. Callers who
  want to switch accounts call `DELETE connections/me` first.
- **Callback replay / expired state** → 400, nothing stored. State is deleted on first use.
- **Callback for a different project than the one in the state** → impossible by design: the
  callback has no project header; everything comes from state.
- **Entra returns `error=access_denied`** → redirect with `driveStatus=failed&reason=access_denied`.
- **`AADSTS65001` in the token exchange or consent** → `driveStatus=admin_consent_required`.
- **Refresh token rejected** (`invalid_grant`) → connection row deleted, operation returns
  `NotConnected`. Status returns `connected: false`.
- **Concurrent requests refreshing the same token** → per-connection in-memory lock so only one
  refresh happens; others wait and reuse. Entra rotates refresh tokens for personal accounts, so the
  new refresh token from every refresh response replaces the stored one.
- **Graph 429/503** → retry up to 3 times honoring `Retry-After` (default 2 s if absent), then
  `Throttled` with the last `retryAfterSeconds`.
- **Graph 404** → `ItemNotFound`.
- **Folder name conflict** → `NameConflict` (Graph `nameAlreadyExists`). No auto-rename for folders.
- **Upload name conflict** → Graph `conflictBehavior=rename`; the response item carries the final name.
- **Upload partial failure** → per-file result, previously uploaded files are left in place.
- **Import above size cap** → `FileTooLarge` before any bytes are moved (Graph reports `size`).
- **Upload above size cap** → per-file `FileTooLarge`, other files continue.
- **Streams, not buffers** → import pipes the Graph download stream into Blocks Storage; upload pipes
  the Blocks Storage stream into a Graph upload session in 10 MB chunks (multiple of 320 KiB, as
  Graph requires). Nothing larger than one chunk is held in memory.
- **Blocks Storage write fails mid-import** → `StorageError`; nothing is left in the drive.
- **Invalid `sortBy`** or `pageSize` outside 1..200 → 400 from FluentValidation.
- **Cursor pointing elsewhere than Graph** → 400 (prevents SSRF through the cursor).
- **Entra config missing from Blocks Secrets** → 503 on connect, logged once at warning.
- **OneDrive Business vs personal** → same Graph routes; `search` on personal accounts can be slow,
  no special handling.

## Non-Functional Requirements

- 100+ concurrent users. Each browse is at most one token refresh plus one Graph call; the in-memory
  access-token cache keyed by connection makes the common case a single Graph call.
- Target p95 for `GET items` under 2 s excluding Graph latency beyond our control; measured by an
  OpenTelemetry histogram `drive.graph.request.duration` tagged with operation and status.
- Counters: `drive.connections.created`, `drive.connections.revoked`, `drive.graph.throttled`,
  `drive.imports.bytes`, `drive.uploads.bytes`.
- Logs never contain tokens, codes, or `returnUrl` query strings; reuse the redaction helper in
  `StorageHelperBase`.
- All times UTC via `TimeProvider`; token expiry uses the controlled clock in tests.
- Named `HttpClient` `utility-drive-graph` with `IHttpClientFactory`; no raw `new HttpClient`.
- No new NuGet packages. Graph is called over REST with `System.Text.Json`; no Graph SDK.

## Integrations

- **Microsoft Entra ID** (`https://login.microsoftonline.com/common/oauth2/v2.0/{authorize,token}`),
  authorization code flow with PKCE and client secret. Config:
  - Blocks Secrets (blocks-os secret store, read at runtime through `ISecretService` for the
    calling project, like `GeolocationApiKeySecretId`): the Entra client secret and the token
    encryption key (32 random bytes, base64). Their ids go in configuration; values are cached
    per project for `SecretCacheSeconds` (300).
  - appsettings `Drive` section: `ClientId`, `RedirectUrl`, `ClientSecretSecretId`,
    `TokenEncryptionKeySecretId`, `AuthorityBaseUrl`, `GraphBaseUrl`, `Scopes`, `MaxFileSizeBytes`
    (250 MB), `UploadChunkSizeBytes` (10 MB), `MaxUploadFilesPerRequest` (20), `StateTtlMinutes`
    (10), `MaxRetries` (3), `DefaultPageSize` (50).
  - The callback arrives with no tenant header, so it runs under the project recorded in the
    OAuth state for the duration of the token exchange; that is what lets the secrets resolve.
- **Microsoft Graph v1.0**: `/me/followedSites`, `/sites?search=`, `/sites/{id}/drive`,
  `/me/drive/root`, `/drives/{d}/items/{id}/children`, `.../search(q=)`, `POST .../children`,
  `GET /drives/{d}/items/{id}` plus `@microsoft.graph.downloadUrl`, `PUT .../content` (≤ 4 MB),
  `POST .../createUploadSession` and chunked `PUT` (> 4 MB).
- **Blocks Storage**: existing `StorageHelperBase`-based helper for reading a file stream by
  `fileId` and for saving a stream under the caller's project.
- **AES-GCM**: a small `DriveTokenProtector` in the Drive module, same payload layout as Payment's
  protector, keyed from the Blocks secret `Drive:TokenEncryptionKeySecretId` names. No dependency
  on Payment or Key Vault.
- **Genesis tenant data**: `Tenant.ApplicationDomain` and `Tenant.AllowedCorsOrigins` via `ITenants`
  for `returnUrl` validation.

## Acceptance Criteria

**Connection**

- AC-1 Given a user with no connection, when they `POST connections` with an allowed `returnUrl`,
  then the response contains an Entra authorize URL with `state`, PKCE `code_challenge`, the
  configured scopes, and a state row exists with a 10-minute TTL.
- AC-2 Given a `returnUrl` whose host is not a registered project domain, when they `POST connections`,
  then 400 and no state row is written.
- AC-3 Given a valid state and Entra returns a code, when the callback is hit, then a connection row
  exists with an encrypted refresh token, the state row is gone, and the response is a 302 to
  `returnUrl?driveStatus=connected`.
- AC-4 Given the same state is sent to the callback twice, when the second call arrives, then 400 and
  the connection from the first call is unchanged.
- AC-5 Given Entra responds `AADSTS65001`, when the callback runs, then 302 with
  `driveStatus=admin_consent_required`, and `GET connections/me` returns `adminConsentUrl`.
- AC-6 Given a connected user, when `GET connections/me`, then `connected: true` with display name
  and `connectedAt`.
- AC-7 Given a connected user, when `DELETE connections/me`, then 204 and a following `GET items`
  returns `NotConnected`.
- AC-8 Given a connected user whose refresh token Entra now rejects with `invalid_grant`, when any
  drive operation runs, then the connection row is deleted and the result is `NotConnected`.
- AC-9 Given a user already connected, when `POST connections` again, then `{ connected: true }` and
  no authorize URL.

**Browse and search**

- AC-10 Given a connected user, when `GET items?source=onedrive`, then root children are returned as
  `DriveItemSummary` with `kind` derived from the Graph `folder`/`file` facets.
- AC-11 Given a folder with more items than `pageSize`, when the first page is requested, then
  `nextCursor` is present, and sending it returns the next page with no repeated items.
- AC-12 Given `sortBy=name&sortOrder=desc`, when listing, then the Graph request carries
  `$orderby=name desc`.
- AC-13 Given `search=contract`, when listing, then the Graph `search(q='contract')` route is used,
  `sortBy` is ignored, and `sortApplied` is `false`.
- AC-14 Given `source=sharepoint&siteId=X`, when listing root, then the Graph route is
  `/sites/X/drive/root/children`.
- AC-15 Given `GET sites` with no `search`, then `/me/followedSites` is called; with `search=hr`,
  then `/sites?search=hr` is called.
- AC-16 Given a `cursor` whose decoded URL host is not the Graph base URL, when listing, then 400 and
  no outbound call is made.
- AC-17 Given Graph answers 429 with `Retry-After: 1` twice then 200, when listing, then the result
  is success and the throttled counter is 2.
- AC-18 Given Graph answers 429 four times, when listing, then `Throttled` with `retryAfterSeconds`
  and exactly 4 outbound calls were made.

**Folders**

- AC-19 Given a connected user, when `POST folders` with a new name, then 201 with the created item
  and the Graph body is `{ name, folder: {}, "@microsoft.graph.conflictBehavior": "fail" }`.
- AC-20 Given the folder name exists, when `POST folders`, then `NameConflict`.

**Import**

- AC-21 Given a 3 MB drive file, when `POST imports`, then the Graph download stream is written to
  Blocks Storage without being fully buffered, and the response carries the new `fileId`, name,
  size, and mime type.
- AC-22 Given a drive file larger than `MaxFileSizeBytes`, when `POST imports`, then `FileTooLarge`
  and no download is started.
- AC-23 Given Blocks Storage fails during the write, when `POST imports`, then `StorageError` and
  no `fileId` is returned.

**Upload**

- AC-24 Given two Blocks Storage files of 1 MB each, when `POST uploads`, then both are sent with
  simple `PUT .../content?@microsoft.graph.conflictBehavior=rename` and both results are
  `success: true` with the created item.
- AC-25 Given a 30 MB Blocks Storage file, when `POST uploads`, then an upload session is created and
  the file is sent in chunks of `UploadChunkSizeBytes`, each with a correct `Content-Range`.
- AC-26 Given three files where the second fails with a Graph 500, when `POST uploads`, then results
  are `[success, error ProviderError, success]` and the request still returns 200.
- AC-27 Given a file name that already exists in the target folder, when `POST uploads`, then the
  result item's `name` is the renamed one Graph returned.
- AC-28 Given 21 `fileIds`, when `POST uploads`, then 400 from validation.

**Security and observability**

- AC-29 Given any log line emitted during connect, callback, or refresh, then it contains no
  `code`, `access_token`, `refresh_token`, or `client_secret` values.
- AC-30 Given a request with a bearer token for user A, when any endpoint is called, then only user
  A's connection row is read or written, regardless of body contents.
- AC-31 Given `Drive:ClientId` is missing from configuration, or either Blocks secret is not
  provisioned for the project, when `POST connections`, then 503 and a warning log.

## Deferred Decisions

| Decision | Fallback chosen now | Revisit when |
|---|---|---|
| Chunked upload of a storage file whose length is unknown | Spill to a temp file to learn the length (`ponytail:` comment in `DriveService`) | If storage ever stops sending `Content-Length` or `SizeInBytes` |
| Encryption key rotation | Single key `drive-v1`; `EncryptionKeyId` is stored so a second key can be introduced later | When security policy requires rotation |
| Access-token cache across API replicas | In-memory per pod; each pod refreshes independently | If Entra token-endpoint rate limits appear under 100+ users |
| Rename / delete / move endpoints | Not built | When a consumer asks |
| Sovereign cloud base URLs | Configurable via appsettings but untested | When a customer on Graph China / US Gov appears |

## Open Questions

None.
