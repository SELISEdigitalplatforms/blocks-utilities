using System.Net.Http.Headers;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>Raw Stripe Invoice API calls — one method per endpoint, no domain interpretation.</summary>
public sealed class StripeInvoiceClient : IStripeInvoiceClient
{
    private readonly IHttpService _httpService;
    private readonly IHttpClientFactory _httpClients;
    private readonly StripeEndpointPolicy _endpointPolicy;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<StripeInvoiceClient> _logger;

    public StripeInvoiceClient(
        IHttpService httpService,
        IHttpClientFactory httpClients,
        StripeEndpointPolicy endpointPolicy,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<StripeInvoiceClient> logger)
    {
        _httpService = httpService;
        _httpClients = httpClients;
        _endpointPolicy = endpointPolicy;
        _options = options;
        _logger = logger;
    }

    public Task<StripeInvoiceCallResult> CreateInvoiceItemAsync(
        PaymentProvider provider,
        string customerId,
        string invoiceId,
        long amountMinor,
        string currencyCode,
        string description,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var form = new StripeForm()
            .Add("customer", customerId)
            // The invoice this line lands on. Without it the line stays pending and recent API
            // versions leave it off the invoice entirely, which finalizes at zero.
            .Add("invoice", invoiceId)
            .Add("amount", amountMinor)
            .Add("currency", currencyCode.ToLowerInvariant())
            .Add("description", description);

        return PostInvoiceObjectAsync(
            provider,
            "v1/invoiceitems",
            form,
            idempotencyKey,
            "invoice item",
            cancellationToken);
    }

    public Task<StripeInvoiceCallResult> CreateInvoiceAsync(
        PaymentProvider provider,
        string customerId,
        string defaultPaymentMethodId,
        string currencyCode,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);

        var form = new StripeForm()
            .Add("customer", customerId)
            // Stated, never inferred. The line item is added after this call, so there is
            // nothing here for Stripe to read a currency from: it reaches for the customer's
            // history and then the merchant account's default, and an item in any other
            // currency cannot attach to what comes back.
            .Add("currency", currencyCode.ToLowerInvariant())
            .Add("collection_method", "charge_automatically")
            // Blocks decides when this advances, not Stripe's own background job — the same
            // discipline that keeps this off a second billing clock. It does not stop the
            // charge at finalization, only Stripe's retries after one.
            .Add("auto_advance", false)
            // Which card settles it, since finalizing collects straight away. Left off, Stripe
            // reaches for the customer's own default and can take a card this renewal never
            // chose.
            .Add("default_payment_method", defaultPaymentMethodId);

        return PostInvoiceObjectAsync(
            provider,
            "v1/invoices",
            form,
            idempotencyKey,
            "invoice",
            cancellationToken);
    }

    public Task<StripeInvoiceCallResult> FinalizeInvoiceAsync(
        PaymentProvider provider,
        string invoiceId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        PostInvoiceObjectAsync(
            provider,
            $"v1/invoices/{Uri.EscapeDataString(invoiceId)}/finalize",
            new StripeForm(),
            idempotencyKey,
            "invoice finalization",
            cancellationToken);

    public Task<StripeInvoiceCallResult> PayInvoiceAsync(
        PaymentProvider provider,
        string invoiceId,
        string paymentMethodId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var form = new StripeForm().Add("payment_method", paymentMethodId);

        return PostInvoiceObjectAsync(
            provider,
            $"v1/invoices/{Uri.EscapeDataString(invoiceId)}/pay",
            form,
            idempotencyKey,
            "invoice payment",
            cancellationToken,
            // A pay call answering with the invoice still open is a decline, not a transport
            // failure — the call itself succeeded, the charge did not.
            requiresPaidStatus: true);
    }

    public async Task VoidInvoiceAsync(
        PaymentProvider provider,
        string invoiceId,
        CancellationToken cancellationToken)
    {
        if (!_endpointPolicy.IsAllowed(provider.ApiBaseUrl))
        {
            return;
        }

        try
        {
            await _httpService.SendFormUrlEncoded<StripeInvoice>(
                HttpMethod.Post,
                new Dictionary<string, string>(StringComparer.Ordinal),
                StripeUrl.Build(
                    provider.ApiBaseUrl,
                    $"v1/invoices/{Uri.EscapeDataString(invoiceId)}/void"),
                StripeRequestHeaders.Create(provider, $"void:{invoiceId}"),
                cancellationToken,
                Math.Clamp(_options.CurrentValue.ProviderTimeoutSeconds, 1, 60));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A void that could not complete leaves an open invoice behind — harmless, since
            // nothing else will ever pay it, just untidy. Not worth failing the caller over.
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                "Voiding a declined Stripe invoice failed InvoiceHash={InvoiceHash} ExceptionType={ExceptionType}",
                PaymentLogValue.Hash(invoiceId),
                exception.GetType().Name);
        }
    }

    public async Task<StripeInvoiceDocument?> DownloadInvoicePdfAsync(
        PaymentProvider provider,
        string invoiceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (!_endpointPolicy.IsAllowed(provider.ApiBaseUrl))
        {
            return null;
        }

        var invoice = await ReadInvoiceAsync(provider, invoiceId, cancellationToken);

        if (invoice?.InvoicePdf is not { Length: > 0 } pdfUrl)
        {
            _logger.LogWarning(
                "A Stripe invoice has no PDF to download InvoiceHash={InvoiceHash}",
                PaymentLogValue.Hash(invoiceId));

            return null;
        }

        // Stripe serves the PDF from files.stripe.com rather than the API host, so the endpoint
        // policy's API check does not cover it. Confirming the host is Stripe's keeps a link out
        // of a response from redirecting this fetch at somewhere else entirely.
        if (!StripeFileUrl.IsStripeHosted(pdfUrl))
        {
            _logger.LogError(
                "A Stripe invoice PDF link pointed somewhere unexpected and was not followed " +
                "InvoiceHash={InvoiceHash}",
                PaymentLogValue.Hash(invoiceId));

            return null;
        }

        try
        {
            using var client = _httpClients.CreateClient(nameof(StripeInvoiceClient));
            client.Timeout = TimeSpan.FromSeconds(
                Math.Clamp(_options.CurrentValue.ProviderTimeoutSeconds, 1, 60));

            using var request = new HttpRequestMessage(HttpMethod.Get, pdfUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                StripeConstants.AuthorizationScheme,
                provider.ApiKey);

            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Downloading a Stripe invoice PDF failed Status={Status} " +
                    "InvoiceHash={InvoiceHash}",
                    (int)response.StatusCode,
                    PaymentLogValue.Hash(invoiceId));

                return null;
            }

            return new StripeInvoiceDocument(
                await response.Content.ReadAsByteArrayAsync(cancellationToken),
                response.Content.Headers.ContentType?.MediaType ?? "application/pdf",
                invoice.Number);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                "Downloading a Stripe invoice PDF failed ExceptionType={ExceptionType} " +
                "InvoiceHash={InvoiceHash}",
                exception.GetType().Name,
                PaymentLogValue.Hash(invoiceId));

            return null;
        }
    }

    private async Task<StripeInvoice?> ReadInvoiceAsync(
        PaymentProvider provider,
        string invoiceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var (invoice, _) = await _httpService.SendFormUrlEncoded<StripeInvoice>(
                HttpMethod.Get,
                new Dictionary<string, string>(StringComparer.Ordinal),
                StripeUrl.Build(
                    provider.ApiBaseUrl,
                    $"v1/invoices/{Uri.EscapeDataString(invoiceId)}"),
                StripeRequestHeaders.Create(provider, $"read:{invoiceId}"),
                cancellationToken,
                Math.Clamp(_options.CurrentValue.ProviderTimeoutSeconds, 1, 60));

            return invoice is { Id: not null, Error: null } ? invoice : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                "Reading a Stripe invoice failed ExceptionType={ExceptionType} " +
                "InvoiceHash={InvoiceHash}",
                exception.GetType().Name,
                PaymentLogValue.Hash(invoiceId));

            return null;
        }
    }

    private async Task<StripeInvoiceCallResult> PostInvoiceObjectAsync(
        PaymentProvider provider,
        string path,
        StripeForm form,
        string idempotencyKey,
        string operation,
        CancellationToken cancellationToken,
        bool requiresPaidStatus = false)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (!_endpointPolicy.IsAllowed(provider.ApiBaseUrl))
        {
            return new StripeInvoiceCallResult(StripeInvoiceOutcome.Unavailable);
        }

        try
        {
            var (invoice, error) = await _httpService.SendFormUrlEncoded<StripeInvoice>(
                HttpMethod.Post,
                form.Fields,
                StripeUrl.Build(provider.ApiBaseUrl, path),
                StripeRequestHeaders.Create(provider, idempotencyKey),
                cancellationToken,
                Math.Clamp(_options.CurrentValue.ProviderTimeoutSeconds, 1, 60));

            if (invoice is { Id: not null, Error: null })
            {
                if (!requiresPaidStatus ||
                    string.Equals(invoice.Status, "paid", StringComparison.Ordinal))
                {
                    return new StripeInvoiceCallResult(
                        StripeInvoiceOutcome.Success,
                        invoice.Id,
                        invoice.Status,
                        AmountMinor: invoice.AmountDue);
                }

                return new StripeInvoiceCallResult(
                    StripeInvoiceOutcome.Rejected,
                    invoice.Id,
                    invoice.Status,
                    ProviderRejectionParser.SanitizeErrorCode(invoice.Status),
                    invoice.AmountDue);
            }

            return Classify(provider, invoice?.Error, error, operation);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new StripeInvoiceCallResult(StripeInvoiceOutcome.Timeout);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Stripe invoice call failed Operation={Operation} Provider={Provider} ExceptionType={ExceptionType}",
                operation,
                PaymentLogValue.Label(provider.ProviderName),
                exception.GetType().Name);

            return new StripeInvoiceCallResult(StripeInvoiceOutcome.OutcomeUnknown);
        }
    }

    private StripeInvoiceCallResult Classify(
        PaymentProvider provider,
        StripeError? stripeError,
        string? transportError,
        string operation)
    {
        if (stripeError != null)
        {
            return new StripeInvoiceCallResult(
                StripeOutcomeMapper.Map(stripeError) switch
                {
                    ProviderClientOutcome.Unavailable => StripeInvoiceOutcome.Unavailable,
                    ProviderClientOutcome.Rejected => StripeInvoiceOutcome.Rejected,
                    _ => StripeInvoiceOutcome.OutcomeUnknown
                },
                SafeErrorCode: StripeOutcomeMapper.SafeCode(stripeError));
        }

        if (ProviderRejectionParser.TryGetValidationErrorCode(
                transportError,
                out var safeErrorCode))
        {
            return new StripeInvoiceCallResult(StripeInvoiceOutcome.Rejected, SafeErrorCode: safeErrorCode);
        }

        _logger.LogWarning(
            "Stripe invoice call returned no usable response Operation={Operation} Provider={Provider} HasPackageError={HasPackageError}",
            operation,
            PaymentLogValue.Label(provider.ProviderName),
            !string.IsNullOrWhiteSpace(transportError));

        return new StripeInvoiceCallResult(
            StripeUnavailable.IsTransient(transportError)
                ? StripeInvoiceOutcome.Unavailable
                : StripeInvoiceOutcome.OutcomeUnknown);
    }
}
