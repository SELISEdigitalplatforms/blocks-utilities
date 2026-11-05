using Microsoft.Extensions.Logging;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// The invoice for a standalone credit purchase (spec 005), composed from the purchase record.
/// </summary>
/// <remarks>
/// Every other invoice here is composed from a subscription: its plan names the subject, its
/// schedule names the period, its snapshot freezes the terms. A standalone purchase has none of
/// that, and needs none: the purchase record was written before the charge with every figure the
/// invoice states, so it is both the obligation and the terms. The period is left empty, which the
/// template already prints as a dash.
/// </remarks>
public sealed partial class SubscriptionFinancialDocumentIssuer
{
    private readonly ISubscriptionCreditPurchaseRepository? _creditPurchases;

    public async Task<FinancialDocumentIssueResult> IssueForCreditPurchaseAsync(
        string tenantId,
        string purchaseId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (_creditPurchases is null)
        {
            return FinancialDocumentIssueResult.Nothing(FinancialDocumentIssueOutcome.UnknownCharge);
        }

        var purchase = await _creditPurchases.GetAsync(tenantId, purchaseId, cancellationToken);

        if (purchase is null || !purchase.IsOrganizationOwned || purchase.PaymentId is not { Length: > 0 } paymentId)
        {
            return FinancialDocumentIssueResult.Nothing(FinancialDocumentIssueOutcome.UnknownCharge);
        }

        if (purchase.TotalMinor <= 0)
        {
            // A code took it to nothing: no money moved, so there is nothing for a document to say.
            return FinancialDocumentIssueResult.Nothing(FinancialDocumentIssueOutcome.ZeroAmount);
        }

        var payment = await _payments.GetByIdAsync(tenantId, paymentId, cancellationToken);

        if (payment is null || !SettledStatuses.Contains(payment.PaymentStatus))
        {
            return FinancialDocumentIssueResult.Nothing(FinancialDocumentIssueOutcome.PaymentNotSettled);
        }

        var sourceKey = FinancialDocumentSourceKey.ForPayment(paymentId);
        var existing = await _documents.FindBySourceKeyAsync(tenantId, sourceKey, cancellationToken);

        if (existing is not null)
        {
            return FinancialDocumentIssueResult.Issued(existing, inserted: false);
        }

        var profile = await _profiles.GetAsync(tenantId, purchase.OrganizationId, cancellationToken);
        var merchant = await _merchants.ResolveAsync(tenantId, cancellationToken);
        var issuedAt = payment.PaymentDate == default ? _time.GetUtcNow().UtcDateTime : payment.PaymentDate.ToUniversalTime();
        var number = await _numbers.AllocateAsync(tenantId, FinancialDocumentType.Invoice, issuedAt.Year, cancellationToken);
        var label = CreditLabelOf(purchase);

        var document = new SubscriptionFinancialDocument
        {
            DocumentNumber = number,
            DocumentType = FinancialDocumentType.Invoice,
            IssuedAtUtc = issuedAt,
            TenantId = tenantId,
            OrganizationId = purchase.OrganizationId,
            // No subscription: the empty string is what the organization-level invoice list filters on.
            SubscriptionId = string.Empty,
            PaymentDetailId = paymentId,
            SourceKey = sourceKey,
            CurrencyCode = purchase.CurrencyCode,
            Merchant = merchant,
            Subscriber = new FinancialDocumentParty
            {
                OrganizationId = purchase.OrganizationId,
                LegalName = profile?.LegalName is { Length: > 0 } legalName ? legalName : purchase.OrganizationId,
                DisplayName = profile?.DisplayName,
                Address = profile?.Address,
                TaxRegistrationId = profile?.TaxRegistrationId
            },
            BillingContact = new FinancialDocumentPerson
            {
                Name = profile?.BillingContactName ?? string.Empty,
                Email = profile?.BillingContactEmail
            },
            InitiatedBy = new FinancialDocumentPerson
            {
                UserId = purchase.BuyerUserId is { Length: > 0 } buyer ? buyer : null,
                Name = profile?.BillingContactName ?? purchase.BuyerUserId
            },
            Subject = new FinancialDocumentSubject
            {
                PlanCode = purchase.MeterKey,
                PlanName = label,
                UnitAmountMinor = purchase.UnitAmountMinor
            },
            Period = new FinancialDocumentPeriod(),
            Amounts = new FinancialDocumentAmounts
            {
                GrossSubtotalMinor = purchase.GrossMinor,
                PromotionalDiscountMinor = purchase.DiscountMinor,
                NetSubtotalMinor = purchase.NetMinor,
                TaxRateBasisPoints = purchase.TaxRateBasisPoints,
                TaxMode = SubscriptionTaxPresentation.Describe(purchase.TaxRateBasisPoints, purchase.TaxMode),
                TaxAmountMinor = purchase.TaxMinor,
                TotalMinor = purchase.TotalMinor,
                PromotionCode = purchase.DiscountCode
            },
            Lines =
            [
                new FinancialDocumentLine
                {
                    Description = label,
                    Quantity = purchase.Quantity,
                    UnitAmountMinor = purchase.UnitAmountMinor,
                    AmountMinor = purchase.GrossMinor,
                    ItemKey = purchase.MeterKey
                }
            ],
            PaymentCard = await PaymentCardAsync(payment, tenantId, purchase.BillingAccountId, cancellationToken)
                .ConfigureAwait(false),
            CorrelationId = correlationId
        };

        var outcome = await _documents.InsertAsync(document, cancellationToken);

        if (!outcome.Inserted)
        {
            return FinancialDocumentIssueResult.Issued(outcome.Document, inserted: false);
        }

        _logger.LogInformation(
            "Standalone credit invoice issued DocumentNumber={DocumentNumber} TenantId={TenantId} " +
            "PurchaseHash={PurchaseHash} TotalMinor={TotalMinor}",
            PaymentLogValue.Label(number),
            PaymentLogValue.Id(tenantId),
            PaymentLogValue.Hash(purchase.ItemId),
            purchase.TotalMinor);

        await ScheduleDeliveryAsync(outcome.Document, cancellationToken);

        return FinancialDocumentIssueResult.Issued(outcome.Document, inserted: true);
    }

    /// <summary>What the line and the subject call the credits, from the name frozen at quote.</summary>
    internal static string CreditLabelOf(SubscriptionCreditPurchase purchase) =>
        string.IsNullOrWhiteSpace(purchase.MeterDisplayName)
            ? $"{purchase.MeterKey} credits"
            : $"{purchase.MeterDisplayName} credits";
}
