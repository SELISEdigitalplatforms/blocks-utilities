using FluentValidation;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Validators;

/// <summary>
/// The campaign rules shared by <see cref="CreateDiscountRequestValidator"/> and
/// <see cref="UpdateDiscountRequestValidator"/>.
/// </summary>
/// <remarks>
/// Structural only — format, range and cross-field agreement a request carries on its own.
/// Anything that needs the catalogue (a named plan or price actually existing, a
/// <see cref="CampaignKind.FirstAnnualPeriod"/> price actually being yearly, an entitlement key
/// actually existing on the plan) is checked in <c>DiscountCatalogueService</c> instead, the same
/// way the existing plan/price applicability check already is — a validator here has no
/// repository to ask, and inventing one would duplicate the exact lookup the service already does
/// for the non-campaign case.
/// </remarks>
public abstract class CampaignDiscountRequestValidator<T> : AbstractValidator<T>
    where T : ICampaignDiscountRequest
{
    protected CampaignDiscountRequestValidator()
    {
        RuleFor(request => request.PercentBasisPoints).NotNull().InclusiveBetween(1, 10_000)
            .When(request => request.Kind == DiscountKind.Percent);
        RuleFor(request => request.AmountMinor).NotNull().GreaterThan(0)
            .When(request => request.Kind == DiscountKind.FixedAmount);
        RuleFor(request => request.CurrencyCode).NotEmpty().Length(3)
            .When(request => request.Kind == DiscountKind.FixedAmount);
        RuleForEach(request => request.ApplicablePlanCodes).NotEmpty().MaximumLength(64);
        RuleForEach(request => request.ApplicablePriceIds).NotEmpty().MaximumLength(64);
        RuleFor(request => request).Must(request =>
            request.Kind != DiscountKind.Percent || request.AmountMinor is null)
            .WithMessage("A percentage discount cannot also define a fixed amount.");
        RuleFor(request => request).Must(request =>
            request.Kind != DiscountKind.FixedAmount || request.PercentBasisPoints is null)
            .WithMessage("A fixed discount cannot also define a percentage.");

        var isCampaign = (Func<T, bool>)(request => request.CampaignKind != CampaignKind.Standard);

        // A campaign has a window; a Standard discount does not, and is governed by the legacy
        // ExpiresAtUtc instead. Requiring one to be present the moment the other is absent is what
        // keeps the two mechanisms from silently blending into each other.
        RuleFor(request => request.ValidFromDate).NotNull()
            .WithMessage("A campaign needs a start date.")
            .When(isCampaign);
        RuleFor(request => request.ValidThroughDate).NotNull()
            .WithMessage("A campaign needs an end date.")
            .When(request => isCampaign(request) &&
                request.CampaignKind != CampaignKind.FreeOpeningCalendarPeriod);
        RuleFor(request => request.TimeZoneId).NotEmpty()
            .WithMessage("A campaign needs a time zone its dates are read in.")
            .When(isCampaign);
        RuleFor(request => request).Must(request =>
                BillingLocalTime.TryFindTimeZone(request.TimeZoneId, out _))
            .WithMessage(request => $"'{request.TimeZoneId}' is not a recognised time zone.")
            .When(request => isCampaign(request) && !string.IsNullOrWhiteSpace(request.TimeZoneId));
        RuleFor(request => request).Must(request =>
                request.ValidFromDate is null || request.ValidThroughDate is null ||
                request.ValidThroughDate >= request.ValidFromDate)
            .WithMessage("The campaign cannot end before it starts.")
            .When(isCampaign);

        RuleFor(request => request.ApplicablePriceIds).Must(prices => prices.Count > 0)
            .WithMessage(
                "This campaign kind needs at least one price named — it prices the opening " +
                "stub and first annual period, or the free opening period, of a specific price, " +
                "not of a plan in general.")
            .When(request =>
                request.CampaignKind is CampaignKind.FirstAnnualPeriod
                    or CampaignKind.FreeOpeningCalendarPeriod);

        // Exactly 100%, and never a fixed amount: a partial reduction or a currency-denominated
        // one both leave a positive figure due on an "activate free of charge" period, which is
        // the one outcome this campaign kind exists to rule out.
        RuleFor(request => request).Must(request =>
                request.Kind == DiscountKind.Percent && request.PercentBasisPoints == 10_000)
            .WithMessage("A free-opening-period campaign must be a 100% reduction, not a partial one.")
            .When(request => request.CampaignKind == CampaignKind.FreeOpeningCalendarPeriod);

        RuleFor(request => request.OneUsePerOrganization).Equal(true)
            .WithMessage("A free-opening-period campaign must be limited to one redemption per organization.")
            .When(request => request.CampaignKind == CampaignKind.FreeOpeningCalendarPeriod);

        RuleFor(request => request.RequiresPaymentMethodUpfront).Equal(true)
            .WithMessage("A free-opening-period campaign must require a payment method before activation.")
            .When(request => request.CampaignKind == CampaignKind.FreeOpeningCalendarPeriod);

        RuleFor(request => request.EntitlementOverrideKey).NotEmpty()
            .WithMessage("A free-opening-period campaign must name the entitlement it temporarily caps.")
            .When(request => request.CampaignKind == CampaignKind.FreeOpeningCalendarPeriod);
        RuleFor(request => request.EntitlementOverrideLimit).NotNull().GreaterThan(0)
            .WithMessage("The temporary entitlement limit must be a positive number.")
            .When(request => request.CampaignKind == CampaignKind.FreeOpeningCalendarPeriod);

        // EntitlementService honours an override for FreeOpeningCalendarPeriod only -- see
        // EntitlementServiceCampaignTests' A_first_annual_period_campaign_never_overrides_an_
        // entitlement_either. Accepting one here for FirstAnnualPeriod would store a value that
        // validates cleanly and then does nothing, the same silent-no-op shape the removed
        // ApplyToOpeningStub flag had. Refused instead, at the one point an author can still
        // notice and drop it.
        RuleFor(request => request).Must(request =>
                request.EntitlementOverrideKey is null && request.EntitlementOverrideLimit is null)
            .WithMessage(
                "A first-annual-period campaign cannot carry a temporary entitlement override -- " +
                "it is never enforced for this campaign kind.")
            .When(request => request.CampaignKind == CampaignKind.FirstAnnualPeriod);

        // A non-campaign discount carries none of this. Refused rather than silently dropped, so a
        // caller who set a campaign field on a Standard discount finds out, instead of the field
        // disappearing without explanation the moment it is saved.
        RuleFor(request => request).Must(request =>
                request.ValidFromDate is null && request.ValidThroughDate is null &&
                string.IsNullOrEmpty(request.TimeZoneId) && !request.OneUsePerOrganization &&
                !request.RequiresPaymentMethodUpfront && request.EntitlementOverrideKey is null)
            .WithMessage("A standard discount cannot carry campaign fields. Set a campaign kind, or clear them.")
            .When(request => !isCampaign(request));
    }
}
