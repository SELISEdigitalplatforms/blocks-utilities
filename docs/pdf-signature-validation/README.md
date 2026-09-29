# Spec 001: PDF Signature Validation (EU DSS)

**Status:** In Review
**Date:** 2026-09-30

## Problem

Services that sign PDFs have no way to check, after the fact, what a signature they produced is
worth: whether it validates, which PAdES level it reaches, and whether the long-term validation
data they embedded is actually used. blocks-utilities' ingestion only *detects* that a signature
exists.

The first consumer is the SELISE signature app (`l3-net-signature-app`, spec 009). It is moving
SeliseAdvanced signing from iText to Apache PDFBox and must show that PDFBox-signed files validate
exactly as the iText ones do: **PAdES-BASELINE-LT, `TOTAL_PASSED`, revocation data taken from the
document's `/DSS`**. Its spec 009 names this capability as a required blocks-utilities contract
and consumes it **report-only**: it never blocks, waits on or rolls back a signature because of it.

## Scope

### In

- Validating the signatures of **PDF files already in storage**, by file id, with the
  **EU DSS** library (Digital Signature Service, European Commission).
- An **asynchronous job per file** with a **status read**, shaped like `/pdf-ingestions`:
  `POST /pdf-signature-validations` → `202`; `POST /pdf-signature-validations/status`.
- A **verdict** per file and per signature (see Domain Model).
- **Trust:** the EU List of Trusted Lists (LOTL) plus configured extra trust anchors.
- **Offline revocation:** only the revocation data the document carries is used.
- An optional **completion notification** per file, as ingestion sends.
- Logs, health check and metrics for the validator.

### Out

- Non-PDF formats (XAdES, CAdES, ASiC).
- Validating uploaded bytes or URLs; input is storage file ids only.
- Changing the file in any way: no augmentation (no `/DSS`, no archive timestamps).
- Storing the full DSS reports (simple / detailed / ETSI validation report).
- Fetching revocation data or certificates online during validation.
- Custom validation policies (DSS's default policy only).
- A UI in the client app.
- Validation history (only the latest verdict per file id is kept).

## Domain Model

### `PdfSignatureValidationJob` (Mongo, one per file id)

Mirrors `PdfIngestionJob`: written when a request is accepted, updated as the worker processes it,
keyed by the file's storage id. **Re-validating a file replaces its job**: it is reset to `Queued`
and the new verdict overwrites the old one, so `/status` always answers for the latest request.

| Field | Meaning |
|---|---|
| `Id` | The file's storage id (key). |
| `Status` | `Queued` → `Processing` → `Completed` or `Failed`; stored as a string (as `PdfIngestionJob`). |
| `MessageCoRelationId`, `UserId`, `TenantId`, `CreatedBy`, `CreateDate`, `LastUpdateDate`, `CompletedDate`, `FileName` | As `PdfIngestionJob`. |
| `ErrorCode`, `ErrorMessage` | Why the job produced no verdict; only when `Failed`. |
| `Verdict` | Once `Completed`. |

Retention: the same as ingestion jobs (no TTL today). Growth is bounded by the number of distinct
files validated, since re-validation overwrites.

### Verdict

**Per file**

| Field | Meaning |
|---|---|
| `SignatureCount` | Signatures found. `0` is a valid, completed verdict. |
| `LowestLevel` | The weakest `SignatureLevel` across signatures (`null` when there are none). |
| `AllPassed` | Every signature's indication is `TOTAL_PASSED` (`false` when there are none). |
| `ValidationTime` | When DSS validated the file (UTC). |
| `TrustedListsLoadedAt` | When the trusted lists used were loaded; shows how fresh the trust was. |

**Per signature** (`Signatures[]`, in document order)

| Field | Meaning |
|---|---|
| `FieldName` | The signature's form field name. |
| `Indication` | `TOTAL_PASSED` / `INDETERMINATE` / `TOTAL_FAILED`. |
| `SubIndication` | DSS sub-indication when not passed (e.g. `NO_CERTIFICATE_CHAIN_FOUND`, `HASH_FAILURE`), else `null`. |
| `SignatureLevel` | PAdES level reached: `PAdES-BASELINE-B`, `-T`, `-LT`, `-LTA`, or `null`/`NOT_ADES`. |
| `SignerSubject` | The signing certificate's subject DN. |
| `ClaimedSigningTime` | The time the signature claims (`/M` or the CMS signing-time). |
| `BestSignatureTime` | The earliest time proven by a timestamp, when there is one. |
| `RevocationOrigin` | Where the signer's revocation data came from: `DssDictionary`, `Cms` (in the signature itself) or `None`. |
| `CoversWholeDocument` | The byte range covers the file up to this signature's revision. |

## API Contract

Both endpoints `[Authorize]`, `ApiResponse<T>` envelope and status-code mapping as
`PdfIngestionsController` / `PdfIngestionApiResults`. Routed through the Blocks gateway as
`utilities/v4/pdf-signature-validations`.

| Verb | Route | Request | Response | Errors |
|---|---|---|---|---|
| POST | `/pdf-signature-validations` | `{ fileIds: string[], messageCoRelationId?: string }` | `202` `{ results: [{ fileId, accepted, status?, errorCode?, errorMessage? }], acceptedCount, rejectedCount, statusUrl, messageCoRelationId }` | `400` empty or more than **50** ids; `503` queue unavailable |
| POST | `/pdf-signature-validations/status` | `{ fileIds: string[] }` | `200` `{ results: [{ fileId, found, status?, isComplete, errorCode?, errorMessage?, requestedAtUtc?, completedAtUtc?, verdict? }] }` | `400` empty or more than **50** ids |

- Each file in a batch is accepted or rejected independently (blank or duplicate id → that entry
  rejected, the rest queued), as ingestion does.
- `/status` answers **every** id asked about: `found: false` for one never submitted (or belonging
  to another tenant).
- `isComplete` is true once `status` is `Completed` or `Failed`.
- With `messageCoRelationId`, a completion notification is sent per file, as for ingestion.

## Authorization

As ingestion: any authenticated caller of the project. The job records the caller's tenant
(project key); `/status` only answers for jobs of the caller's tenant; the worker reads the file
from storage under that project key. No new permission.

## Edge Cases & Failure Modes

| Scenario | Behaviour |
|---|---|
| File has no signature | `Completed`, `SignatureCount = 0`, `AllPassed = false`, `LowestLevel = null`. |
| File not in storage | `Failed`, `input_file_not_found`. |
| File cannot be downloaded | `Failed`, `input_file_unreadable`. |
| Not a PDF | `Failed`, `input_not_pdf`. |
| Needs a password to open | `Failed`, `input_password_protected`. Owner-password-only encryption (opens without a password) validates normally. |
| Signer's CA on no trusted list and not a configured anchor | `Completed`; that signature `INDETERMINATE` / `NO_CERTIFICATE_CHAIN_FOUND`. |
| Document carries no revocation data for the signer | `Completed`; level stops below LT, `RevocationOrigin = None` (online fetching is off by design). |
| Several signatures, some failing | `Completed`; each reported on its own, `AllPassed = false`. |
| A file takes longer than the per-file timeout (default **60 s**, configurable) | `Failed`, `validation_timeout`; the Java process is restarted before the next file. |
| The Java process crashes | `Failed`, `validator_crashed`; restarted before the next file. **No automatic retry**; the caller may re-request. |
| Trusted-list refresh fails | Keep using the lists already loaded; log a warning. |
| No trusted list has ever loaded in this process | Jobs `Failed`, `trust_lists_unavailable` (never a misleading `INDETERMINATE`). |
| Same file id requested again while its job is queued or running | Accepted; the job is reset to `Queued` and the latest request's verdict is the one kept. |
| Notification fails | Logged; the job's recorded outcome is unchanged (as ingestion). |

## Non-Functional Requirements

- **Volume:** a few hundred files per day, bursts of ~100 after bulk signing.
- **Latency:** a verdict within **1–2 minutes** of the request at normal load; a burst of 100 files
  on one worker completes within **5 minutes** — spec 009 polls every 30 s for at most ~5 minutes.
- **Runtime:** **one long-running JVM per worker**, started with the worker, with the trusted lists
  loaded once and refreshed every **24 h** (configurable). The .NET consumer sends it one file at a
  time (JSON over stdin/stdout); the JVM is supervised and restarted on crash or timeout.
- **Concurrency:** one file at a time per worker; scale by worker replicas.
- **Memory:** the JVM's heap is capped by configuration (starting point 512 MB), sized so the
  worker's existing tools keep their headroom.
- **Stateless trust:** nothing persists across worker restarts; the lists are downloaded again at
  start-up.

## Integrations

| Dependency | Change |
|---|---|
| EU DSS (`eu.europa.ec.joinup.sd-dss`, latest stable 6.x at implementation) | **New.** LGPL-2.1, shipped unmodified as jars with a small Java wrapper (`tools/dss/`), compiled in `Dockerfile.worker` the way `tools/pdfbox/*.java` is. Licence recorded in the image. |
| Java 17 runtime | Existing (`openjdk17-jre-headless` in the worker image). |
| EU LOTL (`https://ec.europa.eu/tools/lotl/eu-lotl.xml`) and the national trusted lists it points to | **New outbound HTTPS** from the worker, at start-up and on refresh. The only network access validation needs. |
| Extra trust anchors | **New setting**: PEM certificates in configuration (per environment), e.g. Swisscom's CAs for the SELISE seal. |
| Storage | Existing `PdfStorageHelper`: read only. |
| Queue | **New queue** for validation events, alongside `blocks_pdf_ingestion_listener`. |
| Notifications | Existing `IPdfGeneratorNotificationService`, a new event type for validation completion. |
| Health / metrics | The worker's tool health check gains the validator (process up, trusted-list age). Counters: completed, failed, timed out; gauge: trusted-list age. |
| `l3-net-signature-app` spec 009 | First consumer, report-only; this spec is its "Required blocks-utilities contract". |

## Acceptance Criteria

**AC-1** — Given a batch of 1–50 file ids, when POSTed to `/pdf-signature-validations`, then the
response is `202` with one result per id, each valid id `accepted: true` and a job `Queued`.

**AC-2** — Given 0 or 51+ ids, when POSTed to either endpoint, then `400` with a `fileIds` field
error, and nothing is queued.

**AC-3** — Given a blank or duplicate id in a batch, then that entry is rejected and every other
id is still queued.

**AC-4** — Given a file validated before, when requested again, then its job resets to `Queued`
and, once done, `/status` returns only the new verdict.

**AC-5** — Given ids that were never submitted, or belong to another tenant, when `/status` is
called, then each answers `found: false`, and every id asked about has exactly one result.

**AC-6** — Given the SELISE signature app's legacy-signed reference file (one Swisscom AIS Static
signature, `/DSS` with OCSP and CRL), with Swisscom's CAs configured as extra anchors, when
validated, then `Completed`, `SignatureCount = 1`, `Indication = TOTAL_PASSED`,
`SignatureLevel = PAdES-BASELINE-LT`, `RevocationOrigin = DssDictionary`, and `SignerSubject`
names SELISE Group AG.

**AC-7** — Given a file signed twice in turn with `/DSS` entries for both, when validated, then two
signatures are reported in document order, both `TOTAL_PASSED`, and `AllPassed = true`.

**AC-8** — Given a signed file whose signed bytes were altered, when validated, then that signature
is `TOTAL_FAILED` with sub-indication `HASH_FAILURE`, and `AllPassed = false`.

**AC-9** — Given a signed file without `/DSS` and without revocation data in the CMS, when validated
(online fetching off), then `SignatureLevel` is below `PAdES-BASELINE-LT` and
`RevocationOrigin = None`.

**AC-10** — Given a signature whose CA is neither on a trusted list nor a configured anchor, when
validated, then `INDETERMINATE` / `NO_CERTIFICATE_CHAIN_FOUND`.

**AC-11** — Given a PDF with no signature, then `Completed`, `SignatureCount = 0`,
`AllPassed = false`.

**AC-12** — Given a non-PDF file, a file that needs a password to open, or a missing file, then
`Failed` with `input_not_pdf`, `input_password_protected` or `input_file_not_found`.

**AC-13** — Given a file that takes longer than the per-file timeout, then `Failed` with
`validation_timeout`, and the next file is validated by a fresh Java process.

**AC-14** — Given the trusted lists have never loaded, when a file is processed, then `Failed` with
`trust_lists_unavailable`; given a refresh fails after a successful load, then validation continues
with the loaded lists and a warning is logged.

**AC-15** — Given a validation run, then the file in storage is byte-for-byte unchanged.

**AC-16** — Given a `messageCoRelationId`, when a file's job completes or fails, then one
completion notification is sent for that file; a notification failure leaves the job unchanged.

**AC-17** — Given the worker is up, then its health check reports the validator and the age of the
trusted lists, and the completed / failed / timed-out counters and the trusted-list-age gauge are
exported.

**AC-18** — Given 100 files queued at once on one worker, then every verdict is available within
5 minutes of the first request, and a single file on an idle worker within 1 minute.

## Deferred Decisions

| Decision | Chosen fallback | Revisit trigger |
|---|---|---|
| Job retention | Same as ingestion jobs (no TTL). | Ingestion gets a TTL, or validation jobs grow beyond expectations. |
| Keeping full DSS reports | Not kept. | An audit or support case needs the detailed report. |

## Open Questions

_None._
