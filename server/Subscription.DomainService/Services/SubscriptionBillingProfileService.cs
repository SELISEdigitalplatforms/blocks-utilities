using FluentValidation;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

public sealed class SubscriptionBillingProfileService : ISubscriptionBillingProfileService
{
    private readonly ISubscriptionContextResolver _context;
    private readonly ISubscriptionBillingProfileRepository _profiles;
    private readonly IValidator<UpdateBillingProfileRequest> _validator;
    private readonly ISubscriptionAuditTrail? _audit;

    public SubscriptionBillingProfileService(
        ISubscriptionContextResolver context,
        ISubscriptionBillingProfileRepository profiles,
        IValidator<UpdateBillingProfileRequest> validator,
        ISubscriptionAuditTrail? audit = null)
    {
        _audit = audit;
        _context = context;
        _profiles = profiles;
        _validator = validator;
    }

    public async Task<SubscriptionOperationResult<SubscriptionBillingProfileResponse>> GetAsync(
        string? requestedOrganizationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = await _context.ResolveAsync(
            correlationId,
            requestedOrganizationId,
            cancellationToken);

        if (resolution.Context is not { } context)
        {
            return resolution.ToFailure<SubscriptionBillingProfileResponse>(correlationId);
        }

        var profile = await _profiles.GetAsync(
            context.TenantId,
            context.OrganizationId,
            cancellationToken);

        // An organization that has never been asked gets an empty profile rather than a 404. There
        // is nothing missing about not having answered yet, and a client rendering a form needs the
        // same shape either way — including the list of fields it still has to collect.
        return SubscriptionOperationResult<SubscriptionBillingProfileResponse>.Success(
            Map(profile, context.OrganizationId),
            correlationId);
    }

    public async Task<SubscriptionOperationResult<SubscriptionBillingProfileResponse>> UpdateAsync(
        UpdateBillingProfileRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return SubscriptionOperationResult<SubscriptionBillingProfileResponse>.Failure(
                PaymentFailureKind.Validation,
                "subscription_billing_profile_invalid",
                "The billing profile is invalid.",
                correlationId,
                validation.Errors
                    .GroupBy(error => error.PropertyName)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.ErrorMessage).ToArray()));
        }

        var resolution = await _context.ResolveAsync(
            correlationId,
            request.OrganizationId,
            cancellationToken);

        if (resolution.Context is not { } context)
        {
            return resolution.ToFailure<SubscriptionBillingProfileResponse>(correlationId);
        }

        var address = ToAddress(request.Address);

        // Read first, only so the audit record can say which fields moved.
        var previous = _audit is null
            ? null
            : await _profiles.GetAsync(context.TenantId, context.OrganizationId, cancellationToken);

        var stored = await _profiles.UpsertAsync(
            new SubscriptionBillingProfile
            {
                TenantId = context.TenantId,
                OrganizationId = context.OrganizationId,
                LegalName = request.LegalName.Trim(),
                DisplayName = Trimmed(request.DisplayName),
                BillingContactName = request.BillingContactName.Trim(),
                BillingContactEmail = request.BillingContactEmail.Trim().ToLowerInvariant(),
                BillingContactLanguage = Trimmed(request.BillingContactLanguage),
                // An address whose every line is blank is stored as no address, so a document does
                // not render an empty block that looks like missing data.
                Address = address is not null && !address.IsEmpty() ? address : null,
                TaxRegistrationId = Trimmed(request.TaxRegistrationId)
            },
            cancellationToken);

        // The person who set the profile up is, by acting, someone a document may have to name.
        if (context.UserId is { Length: > 0 } userId)
        {
            await _profiles.RecordContactAsync(
                context.TenantId,
                context.OrganizationId,
                new BillingContact
                {
                    UserId = userId,
                    Name = stored.BillingContactName,
                    Email = stored.BillingContactEmail
                },
                cancellationToken);
        }

        if (_audit is not null)
        {
            await _audit.RecordAsync(
                new SubscriptionAuditEvent
                {
                    TenantId = context.TenantId,
                    OrganizationId = context.OrganizationId,
                    AggregateType = "BillingProfile",
                    AggregateId = context.OrganizationId,
                    OperationId = correlationId,
                    CorrelationId = correlationId,
                    Operation = previous is null ? "BillingProfileCreate" : "BillingProfileUpdate",
                    Stage = "Settings",
                    Outcome = "Changed",
                    Source = "Api",
                    ActorId = context.ActorId,
                    UserId = context.UserId,
                    Detail = DescribeChange(previous, stored),
                    OccurredAtUtc = DateTime.UtcNow
                },
                cancellationToken);
        }

        return SubscriptionOperationResult<SubscriptionBillingProfileResponse>.Success(
            Map(stored, context.OrganizationId),
            correlationId);
    }

    /// <summary>
    /// The names of the fields that changed, never their values: a billing profile holds a person's
    /// name and email, and the audit trail keeps records forever.
    /// </summary>
    internal static string DescribeChange(SubscriptionBillingProfile? before, SubscriptionBillingProfile after)
    {
        if (before is null)
        {
            return "Billing profile created";
        }

        var changed = new List<string>();
        void Compare(string name, object? left, object? right)
        {
            if (!Equals(left, right))
            {
                changed.Add(name);
            }
        }

        Compare(nameof(after.LegalName), before.LegalName, after.LegalName);
        Compare(nameof(after.DisplayName), before.DisplayName, after.DisplayName);
        Compare(nameof(after.BillingContactName), before.BillingContactName, after.BillingContactName);
        Compare(nameof(after.BillingContactEmail), before.BillingContactEmail, after.BillingContactEmail);
        Compare(nameof(after.BillingContactLanguage), before.BillingContactLanguage, after.BillingContactLanguage);
        Compare(nameof(after.TaxRegistrationId), before.TaxRegistrationId, after.TaxRegistrationId);
        Compare(nameof(after.Address), AddressKey(before.Address), AddressKey(after.Address));

        return changed.Count == 0
            ? "Saved with no changes"
            : $"Changed: {string.Join(", ", changed)}";
    }

    private static string? AddressKey(BillingAddress? address) =>
        address is null
            ? null
            : string.Join("|", address.Line1, address.Line2, address.City, address.Region,
                address.PostalCode, address.CountryCode);

    private static SubscriptionBillingProfileResponse Map(
        SubscriptionBillingProfile? profile,
        string organizationId)
    {
        var missing = BillingProfileCompleteness.MissingFields(profile);

        return new SubscriptionBillingProfileResponse
        {
            OrganizationId = organizationId,
            LegalName = profile?.LegalName ?? string.Empty,
            DisplayName = profile?.DisplayName,
            BillingContactName = profile?.BillingContactName ?? string.Empty,
            BillingContactEmail = profile?.BillingContactEmail ?? string.Empty,
            BillingContactLanguage = profile?.BillingContactLanguage,
            Address = profile?.Address is { } address
                ? new BillingAddressResponse
                {
                    Line1 = address.Line1,
                    Line2 = address.Line2,
                    City = address.City,
                    Region = address.Region,
                    PostalCode = address.PostalCode,
                    CountryCode = address.CountryCode
                }
                : null,
            TaxRegistrationId = profile?.TaxRegistrationId,
            IsComplete = missing.Count == 0,
            MissingFields = missing,
            LastUpdatedDateUtc = profile?.LastUpdatedDateUtc
        };
    }

    private static BillingAddress? ToAddress(BillingAddressRequest? request) =>
        request is null
            ? null
            : new BillingAddress
            {
                Line1 = Trimmed(request.Line1),
                Line2 = Trimmed(request.Line2),
                City = Trimmed(request.City),
                Region = Trimmed(request.Region),
                PostalCode = Trimmed(request.PostalCode),
                CountryCode = Trimmed(request.CountryCode)?.ToUpperInvariant()
            };

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
