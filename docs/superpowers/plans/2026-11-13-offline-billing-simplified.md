# Offline Billing, Simplified — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One route that activates a paid plan without a card, invoices that know whether they are Paid or Unpaid, an API to record a payment, and the spec-006 extras (pricing modes, service statements, operator routes, console page) deleted.

**Architecture:** `POST /api/subscriptions/offline` takes the ordinary `CreateSubscriptionRequest`, runs the ordinary `SubscribeAsync` with `offline: true`, and skips the checkout. Documents get a `PaymentStatus` whose default (`Paid = 0`) is what every existing document deserializes to, so no migration. Everything offline-specific that is not "skip the gateway" is deleted and the compiler lists the fallout.

**Tech Stack:** .NET (ASP.NET Core, MongoDB driver, FluentValidation, xUnit + Moq + FluentAssertions); React/TypeScript client (Vite, TanStack Query, vitest).

**Spec:** `docs/superpowers/specs/2026-11-13-offline-billing-simplified-design.md`

## Global Constraints

- New permission name is exactly `blocks-utilities::subscription::manage-offline`; registered in `server/Api/Controllers/README.md`.
- Payment status values are exactly `Paid` and `Unpaid`; status text on PDF, console and mail context is the same two words.
- Console checkbox copy is exactly **"Payment is collected outside Utilities (no card)"**.
- Record-payment permission is the existing `blocks-utilities::subscription::manage-invoice`.
- No request field decides the collection method; only the route does.
- Server tests: `cd server && dotnet test XUnitTest --filter "FullyQualifiedName~<Class>"`. Full: `dotnet build Blocks.slnx && dotnet test XUnitTest`. Client: `cd client && npx vitest run <path>`; full: `npm run build && npm test && npm run lint`.
- Commit messages follow the repo's `type(subscription): ...` style and end with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- Builder's call (spec §2 fallback): credit purchases for offline customers **stay refused**. The purchase path charges through the gateway with a settlement-floor check (`SubscriptionCreditService.cs:580-700`); invoicing credits Unpaid is a new product path, not a cleanup. Task 7 records this in the spec.

## Review Focus

1. An existing document with no `PaymentStatus` field must list as Paid and be matched by `paymentStatus=Paid` — Task 1 test `A_document_written_before_payment_state_existed_lists_as_paid`.
2. Recording a payment on a credit note must be refused, and on an already-Paid invoice must return 200 unchanged — Task 2 tests.
3. A caller without `manage-offline` hitting `/offline` must get 403 from the attribute, and `POST /api/subscriptions` must still ask for `manage` — Task 3 permission test.
4. An offline activation with a zero opening amount must not open a card-setup session (`RequiresCardSetup` path) — Task 3 test `An_offline_activation_never_opens_a_card_setup_session`.
5. Offline subscriptions must now follow price-term changes like card ones (`ExistingSubscriberPriceTerms` skip removed) — Task 4 test.

---

### Task 1: Payment state on financial documents

**Files:**
- Create: `server/Subscription.DomainService/Enums/FinancialDocumentPaymentStatus.cs`
- Modify: `server/Subscription.DomainService/Entities/SubscriptionFinancialDocument.cs` (after `Status`)
- Modify: `server/Subscription.DomainService/Services/SubscriptionFinancialDocumentIssuer.cs:1294-1330` (`ComposeAndIssueAsync` document construction)
- Modify: `server/Subscription.DomainService/Requests/GetFinancialDocumentsRequest.cs`
- Modify: `server/Subscription.DomainService/Repositories/ISubscriptionFinancialDocumentRepository.cs:66-77`, `SubscriptionFinancialDocumentRepository.cs:117-190` (`ListAsync`)
- Modify: `server/Subscription.DomainService/Services/SubscriptionFinancialDocumentHistoryService.cs:225-236, 326-400` (`ListAsync`, `Map`)
- Modify: `server/Subscription.DomainService/Responses/SubscriptionFinancialDocumentResponse.cs`
- Modify: `server/Subscription.DomainService/Services/FinancialDocumentHtmlTemplate.cs:810-826` (`StatusText`)
- Modify: `server/Subscription.DomainService/Services/SubscriptionFinancialDocumentDeliveryService.cs:497-515` (mail context)
- Test: `server/XUnitTest/Subscription/SubscriptionFinancialDocumentHistoryServiceTests.cs`, `SubscriptionFinancialDocumentIssuerTests.cs`, `FinancialDocumentHtmlTemplateTests.cs`

**Interfaces:**
- Produces: `enum FinancialDocumentPaymentStatus { Paid = 0, Unpaid = 1 }`; entity fields `FinancialDocumentPaymentStatus PaymentStatus`, `DateTime? PaidAtUtc`, `string? PaymentReference`, `string? PaymentRecordedByUserId`; `ListAsync(..., FinancialDocumentPaymentStatus? paymentStatus, ...)` (new parameter after `status`); response fields `string PaymentStatus`, `DateTime? PaidAtUtc`, `string? PaymentReference`, `string OrganizationId`; request field `FinancialDocumentPaymentStatus? PaymentStatus`.

- [ ] **Step 1: Write the failing tests**

In `SubscriptionFinancialDocumentHistoryServiceTests.cs`, replace `A_record_invoice_says_it_is_payable_by_bank_transfer_rather_than_passing_for_a_paid_one` with:

```csharp
[Fact]
public async Task A_payment_status_filter_narrows_the_list_and_an_old_document_counts_as_paid()
{
    var unpaid = await StoredAsync("INV-2026-000001", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    unpaid.PaymentStatus = FinancialDocumentPaymentStatus.Unpaid;
    await StoredAsync("INV-2026-000002", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)); // default: Paid

    var paid = await Service().ListAsync(
        new GetFinancialDocumentsRequest { PaymentStatus = FinancialDocumentPaymentStatus.Paid }, "corr-1", CancellationToken.None);
    var open = await Service().ListAsync(
        new GetFinancialDocumentsRequest { PaymentStatus = FinancialDocumentPaymentStatus.Unpaid }, "corr-1", CancellationToken.None);

    paid.Value!.Items.Select(i => i.DocumentNumber).Should().Equal("INV-2026-000002");
    open.Value!.Items.Select(i => (i.DocumentNumber, i.PaymentStatus, i.OrganizationId))
        .Should().Equal(("INV-2026-000001", "Unpaid", OrganizationId));
}
```

(Use the test class's existing organization constant in place of `OrganizationId`.) If the test's fake repository filters in memory, mirror the rule below there: `Paid` matches everything that is not `Unpaid`.

In `SubscriptionFinancialDocumentIssuerTests.cs` add, using the class's existing issue helpers:

```csharp
[Fact] public async Task An_invoice_backed_by_a_payment_is_issued_paid()        // PaymentDetailId set → PaymentStatus == Paid
[Fact] public async Task An_offline_invoice_with_an_amount_is_issued_unpaid()    // no PaymentDetailId, TotalMinor > 0 → Unpaid
[Fact] public async Task A_zero_amount_document_is_issued_paid()                 // no PaymentDetailId, TotalMinor == 0 → Paid
[Fact] public async Task A_credit_note_is_always_paid()                          // CreditNote → Paid
```

In `FinancialDocumentHtmlTemplateTests.cs:456` change the expected row to `<tr><th>Status</th><td>Unpaid</td></tr>` with the document's `PaymentStatus = Unpaid`, and add a sibling asserting a Paid offline invoice renders `<td>Paid</td>`.

- [ ] **Step 2: Run them to verify they fail**

Run: `cd server && dotnet test XUnitTest --filter "FullyQualifiedName~SubscriptionFinancialDocumentHistoryServiceTests|FullyQualifiedName~SubscriptionFinancialDocumentIssuerTests|FullyQualifiedName~FinancialDocumentHtmlTemplateTests"`
Expected: compile errors on `PaymentStatus` / `FinancialDocumentPaymentStatus`.

- [ ] **Step 3: Implement**

- Enum file: `public enum FinancialDocumentPaymentStatus { Paid = 0, Unpaid = 1 }` with a one-line remark that `Paid` is the default so documents written before the field existed read as Paid (they were all card-backed).
- Entity: the four fields; `PaymentStatus` defaults to `Paid`.
- Issuer, in the `new SubscriptionFinancialDocument { ... }` at :1294: `PaymentStatus = documentType == FinancialDocumentType.CreditNote || paymentDetailId is { Length: > 0 } || amounts.TotalMinor <= 0 ? Paid : Unpaid`.
- Repository `ListAsync`: new parameter `FinancialDocumentPaymentStatus? paymentStatus` after `status`. Filter: `Unpaid` → `builder.Eq(d => d.PaymentStatus, Unpaid)`; `Paid` → `builder.Ne(d => d.PaymentStatus, Unpaid)` (an absent field is Paid).
- Request: `public FinancialDocumentPaymentStatus? PaymentStatus { get; set; }`. History service passes it through.
- Response + `Map`: `PaymentStatus = document.PaymentStatus.ToString()`, `PaidAtUtc`, `PaymentReference`, `OrganizationId = document.OrganizationId`. Seats already travel in `Lines` (`ItemKey`, `Quantity`); no new field.
- Template `StatusText`: delete the `OfflineInvoice` arm; the default arm becomes `_ when document.PaymentStatus == Unpaid => "Unpaid"`, then the existing refund switch. (The `ServiceStatement` arm goes in Task 4.)
- Delivery mail context: add `["PaymentStatus"] = document.PaymentStatus.ToString()`. The mail module's template (outside this repo) picks it up by purpose; Task 7 puts that in the rollout.

- [ ] **Step 4: Run the tests to verify they pass**

Same command as Step 2. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add server/Subscription.DomainService server/XUnitTest
git commit -m "feat(subscription): invoices carry a payment status"
```

---

### Task 2: Record a payment against an invoice

**Files:**
- Create: `server/Subscription.DomainService/Requests/RecordFinancialDocumentPaymentRequest.cs`
- Modify: `ISubscriptionFinancialDocumentRepository.cs`, `SubscriptionFinancialDocumentRepository.cs` (next to `TrySetRefundStatusAsync`, :461)
- Modify: `ISubscriptionFinancialDocumentHistoryService.cs`, `SubscriptionFinancialDocumentHistoryService.cs`
- Modify: `server/Api/Controllers/SubscriptionsController.cs` (after `ResendInvoice`, :893-910)
- Modify: `server/Api/Controllers/README.md:49` (`manage-invoice` row)
- Test: `server/XUnitTest/Subscription/SubscriptionFinancialDocumentHistoryServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 entity fields and response fields.
- Produces:
  - `public sealed class RecordFinancialDocumentPaymentRequest { public DateTime? PaidAtUtc { get; set; } public string? Reference { get; set; } public string? OrganizationId { get; set; } }`
  - Repository: `Task<bool> TryRecordPaymentAsync(string tenantId, string documentId, DateTime paidAtUtc, string? reference, string? recordedByUserId, CancellationToken ct)` — updates only where `PaymentStatus == Unpaid`; returns whether a document was modified.
  - Service: `Task<SubscriptionOperationResult<SubscriptionFinancialDocumentResponse>> RecordPaymentAsync(string documentId, RecordFinancialDocumentPaymentRequest request, string correlationId, CancellationToken ct)`.
  - Route: `POST /api/subscriptions/invoices/{documentId}/payment`, `[ProtectedEndPoint("blocks-utilities::subscription::manage-invoice")]`, action name `RecordInvoicePayment`.
  - Error codes: `financial_document_not_found` (NotFound), `financial_document_not_collectable` (Conflict, credit notes).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Recording_a_payment_moves_an_unpaid_invoice_to_paid_with_the_reference_and_actor()
// store Unpaid INV; RecordPaymentAsync(id, new() { Reference = "TRF-42", PaidAtUtc = 2026-03-01 }) →
// Value.PaymentStatus == "Paid", PaidAtUtc == 2026-03-01, PaymentReference == "TRF-42"; stored PaymentRecordedByUserId == context user id

[Fact] public async Task Recording_a_payment_twice_returns_the_paid_invoice_unchanged()
// second call with another reference → IsSuccess, PaymentReference still "TRF-42", PaidAtUtc unchanged

[Fact] public async Task A_payment_cannot_be_recorded_on_a_credit_note()
// CreditNote → ErrorCode "financial_document_not_collectable", FailureKind Conflict

[Fact] public async Task A_payment_on_another_organizations_document_is_not_found()
// document stored under org-2, caller org-1 → ErrorCode "financial_document_not_found"

[Fact] public async Task A_payment_with_no_date_is_recorded_at_now()
// PaidAtUtc null → equals the test clock's now
```

Follow the class's existing fakes (`StoredAsync`, `Service()`, the time provider it already injects).

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test XUnitTest --filter "FullyQualifiedName~SubscriptionFinancialDocumentHistoryServiceTests"`
Expected: compile errors on `RecordPaymentAsync`.

- [ ] **Step 3: Implement**

Service `RecordPaymentAsync`: resolve context with `request.OrganizationId` (as `ListAsync` does); `GetAsync(tenantId, documentId)`; not found or `document.OrganizationId != context.OrganizationId` → `financial_document_not_found`; `CreditNote` → `financial_document_not_collectable`; already Paid → `Success(Map(document, request.OrganizationId))`; else `TryRecordPaymentAsync(tenant, id, request.PaidAtUtc?.ToUniversalTime() ?? now, request.Reference?.Trim(), context.UserId, ct)`, re-read, map.

Repository `TryRecordPaymentAsync`: `UpdateOneAsync` with filter Tenant + ItemId + `PaymentStatus == Unpaid`, update sets `PaymentStatus = Paid`, `PaidAtUtc`, `PaymentReference`, `PaymentRecordedByUserId`, `LastUpdatedDateUtc`; return `ModifiedCount > 0`.

Controller action: mirror `ResendInvoice`, plus `AuditAsync("RecordInvoicePayment", request.OrganizationId, result.Value?.SubscriptionId, result.IsSuccess, result.ErrorCode, result.FailureKind.ToString(), correlationId, result.Value?.Amounts.TotalMinor, result.Value?.CurrencyCode, cancellationToken)`.

README row 49: `POST /subscriptions/invoices/{id}/resend`, `POST /subscriptions/invoices/{id}/payment`.

- [ ] **Step 4: Run to verify they pass**

Same filter. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add server
git commit -m "feat(subscription): record a payment against an unpaid invoice"
```

---

### Task 3: One request, two routes, one permission

**Files:**
- Modify: `server/Subscription.DomainService/Services/ISubscriptionCheckoutService.cs:11-14`, `SubscriptionCheckoutService.cs` (delete `ActivateOfflineAsync` :99-176 and `_offlineValidator`; edit `SubscribeAsync` :179-245; `OfflineOpeningInvoice` :1289)
- Modify: `ISubscriptionCreationService.cs` (delete `CreateOfflineAsync` :25-40), `SubscriptionCreationService.cs:125-135, 150, 185, 602-626, 754, 835-840, 853, 2014-2032`
- Modify: `server/Api/Controllers/SubscriptionsController.cs:142-178`
- Modify: `server/Subscription.DomainService/Entities/SubscriptionDetail.cs:72-79` (delete `ActivatedBy`), `SubscriptionResponseMapper.cs:79-80`, `Responses/SubscriptionResponse.cs:43-47`
- Modify: `server/Api/Controllers/README.md` (new row)
- Test: `server/XUnitTest/Subscription/SubscriptionsControllerOfflinePermissionTests.cs`, `server/XUnitTest/Subscription/Offline/OfflineActivationTests.cs`

**Interfaces:**
- Produces: `Task<SubscriptionOperationResult<SubscriptionResponse>> SubscribeAsync(CreateSubscriptionRequest request, string correlationId, CancellationToken cancellationToken, bool offline = false)` on `ISubscriptionCheckoutService`; `Task<SubscriptionOperationResult<SubscriptionDetail>> CreateAsync(CreateSubscriptionRequest request, SubscriptionContext context, string correlationId, CancellationToken cancellationToken, bool offline = false)` on `ISubscriptionCreationService`. Controller action `ActivateOffline([FromBody] CreateSubscriptionRequest request, CancellationToken ct)` on `[HttpPost("offline")]` with `[ProtectedEndPoint("blocks-utilities::subscription::manage-offline")]`.
- Consumed by Task 4 (which deletes the pricing types this task stops using).

- [ ] **Step 1: Rewrite the permission test**

Replace the theory in `SubscriptionsControllerOfflinePermissionTests.cs` with two facts using the same reflection helper: `ActivateOffline` asks exactly `blocks-utilities::subscription::manage-offline` ("the only gate on a paid plan with no card is a grant the operator hands out"); `Subscribe` asks exactly `blocks-utilities::subscription::manage`.

- [ ] **Step 2: Rewrite `OfflineActivationTests` against `SubscribeAsync(offline: true)`**

Keep the class's mocks; `_creation.Setup(s => s.CreateAsync(It.IsAny<CreateSubscriptionRequest>(), It.IsAny<SubscriptionContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), true))` returns an Active-ready subscription with `CollectionMethod = OfflineInvoice`. Tests:

```csharp
[Fact] An_offline_activation_is_live_at_once_and_returns_no_checkout_url()
// Value.CheckoutUrl null; captured transition To == Active; CollectionMethod response "OfflineInvoice"
[Fact] An_offline_activation_never_reaches_the_gateway()
// _payments and _setups are MockBehavior.Strict with no setups: any call throws
[Fact] An_offline_activation_never_opens_a_card_setup_session()
// subscription where SubscriptionAmountCalculator.RequiresCardSetup(...) would be true (opening amount 0, paid price) → still StartWithoutPayment, _setups untouched
[Fact] An_offline_opening_period_leaves_an_unpaid_record_invoice_source()
// transition.DocumentSource != null, DocumentType Invoice, Amounts.TotalMinor == opening amount
[Fact] A_card_activation_still_goes_to_checkout()
// offline: false, positive amount → ChargeAsync path; Value.CheckoutUrl not null
```

Delete the console-only test.

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test XUnitTest --filter "FullyQualifiedName~OfflineActivationTests|FullyQualifiedName~SubscriptionsControllerOfflinePermissionTests"`
Expected: compile errors (`SubscribeAsync` has no `offline`).

- [ ] **Step 4: Implement**

- `ISubscriptionCreationService.CreateAsync(..., bool offline = false)`; delete `CreateOfflineAsync`. `CreateCoreAsync` takes `bool offline` instead of `OfflinePricing? offline`; every `offline is not null` becomes `offline`, `offline is null` becomes `!offline`. At :835 replace `OfflineSubscriptionTerms.Apply(subscription, offline)` with `subscription.CollectionMethod = SubscriptionCollectionMethod.OfflineInvoice;`. Delete the Contract calendar refusal :606-626. `PurchasedBy` stays the buyer (Klax's own user or the console operator); :853 becomes `subscription.PurchasedBy is { } buyer`.
- `ISubscriptionCheckoutService.SubscribeAsync(..., bool offline = false)`. In the implementation pass `offline` to `CreateAsync`; after `var subscription = created.Value!` and before `RequiresPayment(amountMinor)`: `if (offline) return await StartWithoutPaymentAsync(subscription, context, correlationId, cancellationToken);`. Delete `ActivateOfflineAsync`, `_offlineValidator`, its constructor parameter. `OfflineOpeningInvoice` :1305 passes `subscription.PurchasedBy`.
- Delete `SubscriptionDetail.ActivatedBy`, `SubscriptionResponse.ActivatedByUserId/ActivatedByName`, mapper lines.
- Controller: `ActivateOffline([FromBody] CreateSubscriptionRequest request, ...)` → `_checkout.SubscribeAsync(request, correlationId, cancellationToken, offline: true)`; attribute `manage-offline`; keep the audit line with action `"ActivateOffline"`. Remarks: one sentence, "Same request as Subscribe; the route is the option, the permission is the gate."
- README: row `blocks-utilities::subscription::manage-offline` → `POST /subscriptions/offline`, with the note "must be created in IAM and granted to the Klax integration role and the console admin role before release".
- Fix other compile fallout (`ApplicationServiceCollectionExtensions.cs:243-245` validator registrations go in Task 4; for now keep the build green by deleting the `ActivateOfflineSubscriptionRequestValidator` registration only if it no longer compiles).

- [ ] **Step 5: Run to verify they pass, then build the solution**

Run: same filter → PASS. Then `dotnet build Blocks.slnx` → 0 errors.

- [ ] **Step 6: Commit**

```bash
git add server
git commit -m "feat(subscription): offline activation takes the ordinary request behind its own permission"
```

---

### Task 4: Delete pricing modes, operator routes and service statements

**Files:**
- Delete: `server/Subscription.DomainService/Entities/OfflinePricing.cs`, `Enums/OfflinePricingMode.cs`, `Services/OfflineSubscriptionTerms.cs`, `Requests/ActivateOfflineSubscriptionRequest.cs`, `Requests/OfflineSubscriptionChangeRequests.cs`, `Validators/ActivateOfflineSubscriptionRequestValidator.cs`
- Modify: `Entities/SubscriptionDetail.cs` (delete `OfflinePricing`, `IsNoPrice`; keep `IsOffline`), `Entities/PendingPlanChange.cs:76-91` (delete `OfflinePricing`, `ScheduledByOperator`), `Repositories/SubscriptionTransition.cs:165-168`, `Repositories/ISubscriptionRepository.cs:174` + `SubscriptionRepository.cs:519,587,1398` (drop `offlinePricing` parameter)
- Modify: `Services/ISubscriptionPlanChangeService.cs:43-66` (delete `ChangePlanOfflineAsync`, `RepriceOfflineAsync`), `SubscriptionPlanChangeService.cs` (delete those methods, `_offlinePricingValidator`, the `asOperator`/`targetPricing`/`TargetPricingOf` plumbing, the console-only refusals at :451-460 and :698-731, the `IsNoPrice` branch :916-919, the `noPriceStatement` term :1616-1618, `CreateOfflinePricingChanged` call :1502-1506)
- Modify: `SubscriptionQuantityChangeService.cs:796-798` (drop `noPriceStatement`), `SubscriptionRenewalService.cs:265-268, 781, 922` (drop `OfflinePricing` install and `IsNoPrice`), `SubscriptionCreationService.cs` (any leftovers), `Outbox/SubscriptionUsageRatingProcessor.cs:708` (`var owed = total > 0;`), `Services/ExistingSubscriberPriceTerms.cs:124` (drop `subscription.IsOffline ||` and the remark at :21)
- Modify: `Enums/FinancialDocumentType.cs` (delete `ServiceStatement`), `Repositories/FinancialDocumentNumberAllocator.cs:75` (delete `STM`), `Services/SubscriptionFinancialDocumentIssuer.cs:476-479, 830, 879-925` (delete the statement branch and `IssueServiceStatementAsync`; trial is always `TrialInvoice`), `Services/SubscriptionDocumentSourceFactory.cs:129-160` (`DocumentType = Invoice`, drop `statement`), `Services/FinancialDocumentHtmlTemplate.cs` (delete `ServiceStatement` arms, `IsStatement` and its three callers at :77, :223, :297 — the statement-only layout branches go, the invoice layout stays), `Reporting/SubscriptionReportingService.cs:608, 995`, `Reporting/SubscriptionReportResponses.cs:424-425`
- Modify: `Outbox/ISubscriptionOutboxEventFactory.cs:47-51`, `SubscriptionOutboxEventFactory.cs:176-192` (delete `CreateOfflinePricingChanged`), `Utilities/SubscriptionConstants.cs:84-85`, `Entities/SubscriptionLifecycleEvent.cs:133-136` (delete `OfflinePricingMode`, `ContractAmountMinor`), `Responses/SubscriptionResponse.cs:27-41` (delete the four pricing fields), `Responses/PendingPlanChangeResponse.cs:36`, `Services/SubscriptionResponseMapper.cs:75-78, 139`, `Utilities/ApplicationServiceCollectionExtensions.cs:243-245`
- Modify: `server/Api/Controllers/SubscriptionsController.cs:378-460` (delete `ChangePlanOffline`, `RepriceOffline`), `SubscriptionReportsController.cs` (remark only)
- Modify: `Simulation/SubscriptionSimulationService.cs:412-416, 779-782, 798` — wording only: drop "spec 006"/"bank transfer"; say "billed by invoice; nothing is charged".
- Test: delete `XUnitTest/Subscription/Offline/OfflineSubscriptionRenewalTests.cs` cases on Contract/None pricing (keep the "renewal skips the gateway and issues an invoice" cases); delete the statement test in `FinancialDocumentHtmlTemplateTests.cs:463-480`; trim `Integration/OfflineSubscriptionIntegrationTests.cs` to the catalogue path through `SubscribeAsync(offline: true)`; fix compile fallout in `SubscriptionPlanChangeServiceTests.cs:2200-2240, 2684`, `SubscriptionCreationServiceTests.cs`, `SubscriptionReportingServiceTests.cs`, `SubscriptionFinancialDocumentIssuerTests.cs`, `SubscriptionQuantityChangeServiceTests.cs`, `SubscriptionUsageRatingProcessorTests.cs`, `ExistingSubscriberPriceTermsTests.cs`, `Integration/*` by deleting what tested a deleted behaviour.

**Interfaces:**
- Consumes: Task 3's `SubscribeAsync(offline: true)`.
- Produces: nothing new. `IsOffline` remains the single offline predicate.

- [ ] **Step 1: Write the one new test**

In `ExistingSubscriberPriceTermsTests.cs`, add `An_offline_subscription_follows_a_price_terms_change_like_a_card_one`: an `OfflineInvoice` subscription on the changed price is **not** `Outcome.Skipped`. Mirror the file's existing card-subscriber case.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test XUnitTest --filter "FullyQualifiedName~ExistingSubscriberPriceTermsTests"`
Expected: FAIL (Skipped).

- [ ] **Step 3: Delete the files, then build and follow the compiler**

Run `dotnet build Blocks.slnx` repeatedly; each error is a line in the Files list above. Rules while fixing: when a method had an `asOperator`/`targetPricing`/`offlinePricing` parameter, remove the parameter and the branches that needed it, never stub it; when a test asserted a deleted behaviour, delete the test; when a test only constructed a deleted type, drop the construction.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test XUnitTest`
Expected: PASS. `grep -rn "OfflinePricing\|IsNoPrice\|ServiceStatement\|ScheduledByOperator\|subscription_offline_console_only" server --include=*.cs | grep -v /obj/` returns nothing.

- [ ] **Step 5: Commit**

```bash
git add -A server
git commit -m "refactor(subscription): delete offline pricing modes, operator routes and service statements"
```

---

### Task 5: Console — subscribe checkbox, offline page removed

**Files:**
- Delete: `client/app/routes/dashboard/offline-billing.tsx`, `client/app/cross-modules/utilities/subscription-simulation/pages/offline-billing-page.tsx` + `.test.tsx`, `hooks/use-offline-billing.ts`, `components/offline-pricing-fields.tsx`, `utilities/offline-pricing.ts` + `.test.ts`, `e2e/tests/03-subscriptions/offline-billing.spec.ts`, `e2e/page/subscripptions/offline-billing.ts`
- Modify: `client/app/router.tsx:24, 185-189`, `client/app/constants/navigation-menus.ts:116-122`, `subscription-simulation/index.ts:3`, `e2e/support/auth-helpers.ts:74,103`, `e2e/support/utilities-helpers.ts:37` (drop `"Offline billing"`)
- Modify: `subscription-simulation/models/subscription-simulation.model.ts:143-193` (delete `OfflinePricingMode`, `OFFLINE_PRICING_MODE_VALUE`, `OfflinePricingRequest`, `ActivateOfflineSubscriptionRequest`, `OfflineRepricingRequest`, `offlinePricingMode`, `pendingOfflinePricingMode`, `contractAmountMinor`, `pendingContractAmountMinor`, `activatedBy*`; keep `CollectionMethod`, `isOfflineSubscription`)
- Modify: `services/subscription-simulation.service.ts:204-260` (delete `activateOffline`, `repriceOffline`, `sendOffline`; `subscribe` gains `offline`)
- Modify: `hooks/use-subscribe-to-plan.ts`, `components/subscribe-dialog.tsx`, `components/current-subscription-card.tsx:13-14, 125-192, 266-275` (keep the badge; move `BilledByInvoiceBadge` into this file; drop pricing summaries and the Contract "per user" special case), `components/credits-card.tsx` (`purchaseRefused` stays — credit purchase remains refused, see Global Constraints), `pages/subscription-simulation-page.tsx`
- Modify: `client/app/i18n/utilities.subscription-simulation.en.json` (or wherever the Simulation module's keys live — `grep -rl SUBSCRIBE_DIALOG client/app/i18n`): add `SUBSCRIBE_DIALOG.OFFLINE_CHECKBOX`; remove keys only the deleted files used (`npm run lint` / the i18n test will name them)
- Test: `components/subscribe-dialog.test.tsx`, `current-subscription-card.test.tsx`

**Interfaces:**
- Produces: `subscriptionSimulationService.subscribe(request: SubscribeToPlanRequest, offline = false): Promise<SimulatedSubscription>` posting to `${SUBSCRIPTIONS_ENDPOINT}/offline` when `offline`; `useSubscribeToPlan()` mutation takes `{ request: SubscribeToPlanRequest; offline: boolean }`.

- [ ] **Step 1: Write the failing dialog test**

In `subscribe-dialog.test.tsx`, following the file's render helper and service mock:

```ts
it("posts to the offline route when payment is collected outside Utilities", async () => {
  // render, pick plan/price as the existing happy-path test does
  await user.click(screen.getByLabelText("Payment is collected outside Utilities (no card)"));
  await user.click(screen.getByRole("button", { name: /subscribe/i }));
  expect(subscribeSpy).toHaveBeenCalledWith(expect.objectContaining({ planCode }), true);
});
it("posts to the ordinary route when the box is left unticked", ...) // second arg false
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd client && npx vitest run app/cross-modules/utilities/subscription-simulation/components/subscribe-dialog.test.tsx`
Expected: FAIL (no such label).

- [ ] **Step 3: Implement**

- Service `subscribe(request, offline = false)`: endpoint `offline ? \`${SUBSCRIPTIONS_ENDPOINT}/offline\` : SUBSCRIPTIONS_ENDPOINT`; same response handling.
- Hook: `mutationFn: ({ request, offline }) => subscriptionSimulationService.subscribe(request, offline)`.
- Dialog: `const [offline, setOffline] = useState(false)`; a `Checkbox` + `Label` (the project's own components, as other dialogs use) with the exact copy; pass `offline` to the mutation; reset on close. Always shown; a 403 surfaces through the dialog's existing error toast.
- Delete the files and references; `npm run build` lists the rest.

- [ ] **Step 4: Verify**

Run: `npx vitest run app/cross-modules/utilities/subscription-simulation && npm run build && npm run lint`
Expected: PASS, 0 errors. `grep -rn "offlinePricing\|OfflinePricing\|activateOffline\|ServiceStatement" client/app e2e` returns nothing.

- [ ] **Step 5: Commit**

```bash
git add -A client e2e
git commit -m "feat(client): offline billing is a checkbox on the subscribe dialog"
```

---

### Task 6: Console — invoices show payment status, filter by it, record a payment

**Files:**
- Modify: `client/app/cross-modules/utilities/subscription/models/subscription-billing.model.ts:125-127, 195-210` (`FinancialDocumentType` loses `ServiceStatement`; add `export type FinancialDocumentPaymentStatus = "Paid" | "Unpaid"`; row gains `paymentStatus: FinancialDocumentPaymentStatus`, `paidAtUtc?: string | null`, `paymentReference?: string | null`, `organizationId: string`; list request gains `paymentStatus?: FinancialDocumentPaymentStatus | null`)
- Modify: `services/subscription-billing.service.ts:127-170` (`listDocuments` passes `paymentStatus`; add `recordDocumentPayment(documentId: string, body: { reference?: string; organizationId?: string }): Promise<SubscriptionFinancialDocument>` → `POST ${SUBSCRIPTION_INVOICES_ENDPOINT}/${id}/payment`)
- Modify: the hooks file that exports `useResendFinancialDocument` (add `useRecordFinancialDocumentPayment` invalidating the documents query)
- Modify: `utilities/financial-document-format.ts:35-62` (`describeDocumentStatus`: drop the `ServiceStatement` and `OfflineInvoice` branches; before the refund switch `if (document.paymentStatus === "Unpaid") return t("FINANCIAL_DOCUMENT_FORMAT.STATUS_UNPAID")`), `financial-document-format.test.ts`
- Modify: `pages/subscription-invoices-page.tsx:52, 124, 170-210, 350-445` (badge variant: `Unpaid` → `"destructive"`; a payment-status `Select` beside the status filter with Any/Paid/Unpaid; a **Record payment** button on rows where `paymentStatus === "Unpaid" && documentType !== "CreditNote"`, opening a small dialog with an optional reference field, same confirm pattern as the resend dialog)
- Modify: `client/app/i18n/utilities.subscription.en.json` (add `FINANCIAL_DOCUMENT_FORMAT.STATUS_UNPAID: "Unpaid"`, `SUBSCRIPTION_INVOICES_PAGE.RECORD_PAYMENT`, `...PAYMENT_REFERENCE`, `...PAYMENT_STATUS_FILTER`, `...PAID`, `...UNPAID`; delete `STATUS_PAYABLE_BY_BANK_TRANSFER`, `STATUS_PRICED_UNDER_CONTRACT`)
- Test: `financial-document-format.test.ts`, `subscription-invoices-page.test.tsx`

- [ ] **Step 1: Write the failing tests**

`financial-document-format.test.ts`: `describeDocumentStatus({ documentType: "Invoice", status: "Issued", paymentStatus: "Unpaid" })` → `"Unpaid"`; same with `"Paid"` → `"Paid"`; delete the bank-transfer and statement cases.

`subscription-invoices-page.test.tsx`, using the file's existing service mock and render:
```ts
it("offers Record payment only on an unpaid invoice", ...)   // Unpaid Invoice row has the button; Paid row and CreditNote row do not
it("records a payment with the reference and refreshes the list", ...) // click → dialog → type "TRF-42" → confirm → recordDocumentPayment called with (documentId, { reference: "TRF-42", organizationId })
it("filters by payment status", ...)  // choose Unpaid → listDocuments called with paymentStatus "Unpaid"
```

- [ ] **Step 2: Run to verify they fail**

Run: `npx vitest run app/cross-modules/utilities/subscription`
Expected: FAIL.

- [ ] **Step 3: Implement** per the Files list. Reuse the resend dialog's structure for the record-payment dialog rather than a new component file unless it exceeds ~60 lines.

- [ ] **Step 4: Verify**

Run: `npx vitest run app/cross-modules/utilities/subscription && npm run build && npm run lint`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add client
git commit -m "feat(client): invoices show payment status and take a recorded payment"
```

---

### Task 7: Docs, spec note, rollout, whole-tree verification

**Files:**
- Modify: `docs/specs/006-offline-plan-activation/README.md` (top note: "Superseded on 2026-11-13 by `docs/superpowers/specs/2026-11-13-offline-billing-simplified-design.md`: pricing modes, service statements, operator routes and the console page were removed.")
- Modify: `docs/superpowers/specs/2026-11-13-offline-billing-simplified-design.md` §2 and "Left to the builder": record the credit-purchase call (stays refused; reason: purchase is a gateway charge with a settlement floor, invoicing credits Unpaid is a new product path). §7 Rollout: add "the mail module's `subscription_invoice` template must print `{{PaymentStatus}}`; until it does the email body says nothing about payment".
- Modify: `server/Api/Controllers/README.md` — confirm both rows from Tasks 2 and 3 are present.

- [ ] **Step 1: Make the doc edits**

- [ ] **Step 2: Whole-tree verification**

Run: `cd server && dotnet build Blocks.slnx && dotnet test XUnitTest` → PASS.
Run: `cd client && npm run build && npm test && npm run lint` → PASS.
Run: `grep -rn "spec 006\|Spec 006" server client/app --include=*.cs --include=*.ts --include=*.tsx | grep -v /obj/ | wc -l` → every remaining hit is a gateway-skip remark in `SubscriptionRenewalService`, `SubscriptionSettlementReservationProcessor`, `SubscriptionUsageRatingProcessor`, `SubscriptionQuantityChangeService`, `SubscriptionPlanChangeService` or `SubscriptionSimulationService`; rewrite any other to plain English.

- [ ] **Step 3: Commit**

```bash
git add docs server/Api/Controllers/README.md
git commit -m "docs(subscription): offline billing simplified; spec 006 superseded"
```
