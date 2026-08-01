using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Enums;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("payments/providers")]
public sealed class PaymentProvidersController : ControllerBase
{
    private readonly IPaymentProviderQueryService _queryService;
    private readonly IPaymentProviderConfigurationService
        _configurationService;
    private readonly IPaymentProviderCredentialRotationService
        _credentialRotationService;
    private readonly IPaymentEncryptionAdminService _encryptionService;

    public PaymentProvidersController(
        IPaymentProviderQueryService queryService,
        IPaymentProviderConfigurationService configurationService,
        IPaymentProviderCredentialRotationService credentialRotationService,
        IPaymentEncryptionAdminService encryptionService)
    {
        _queryService = queryService;
        _configurationService = configurationService;
        _credentialRotationService = credentialRotationService;
        _encryptionService = encryptionService;
    }

    /// <summary>
    /// Lists the calling tenant's payment providers without credentials.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(
        typeof(ApiResponse<IReadOnlyList<PaymentProviderResponse>>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ApiResponse<object>),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetPaymentProviders(
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _queryService.GetProvidersAsync(
            correlationId,
            cancellationToken);

        return result.IsSuccess
            ? Ok(ApiResponse<
                IReadOnlyList<PaymentProviderResponse>>.Ok(
                    result.Providers,
                    correlationId))
            : ListFailure(
                result.FailureKind,
                result.ErrorCode,
                result.ErrorMessage,
                correlationId);
    }

    /// <summary>
    /// Replaces editable non-secret configuration when the supplied version
    /// is still current.
    /// </summary>
    [HttpPut("{paymentProviderId}")]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentProviderResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentProviderResponse>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentProviderResponse>),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentProviderResponse>),
        StatusCodes.Status409Conflict)]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentProviderResponse>),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UpdatePaymentProvider(
        string paymentProviderId,
        [FromBody] UpdatePaymentProviderRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _configurationService.UpdateAsync(
            paymentProviderId,
            request,
            correlationId,
            cancellationToken);

        return result.IsSuccess
            ? Ok(ApiResponse<PaymentProviderResponse>.Ok(
                result.Provider!,
                correlationId))
            : Failure(result);
    }

    /// <summary>
    /// Rotates provider credentials. A rotated webhook secret retains the
    /// previous active value for signature-verification overlap.
    /// </summary>
    [HttpPost("{paymentProviderId}/rotate")]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentProviderResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentProviderResponse>),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentProviderResponse>),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentProviderResponse>),
        StatusCodes.Status409Conflict)]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentProviderResponse>),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RotatePaymentProviderCredentials(
        string paymentProviderId,
        [FromBody] RotatePaymentProviderCredentialsRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _credentialRotationService.RotateAsync(
            paymentProviderId,
            request,
            correlationId,
            cancellationToken);

        return result.IsSuccess
            ? Ok(ApiResponse<PaymentProviderResponse>.Ok(
                result.Provider!,
                correlationId))
            : Failure(result);
    }

    /// <summary>
    /// Reports whether this organization's encryption key ring can be read.
    /// </summary>
    /// <remarks>
    /// With a key ring per organization there is nothing to check at startup, so this is how an
    /// operator confirms a newly provisioned ring — or diagnoses a broken one — without putting
    /// a payment through. It reports the expected vault secret name, never any key material.
    /// </remarks>
    [HttpGet("encryption")]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentEncryptionHealthResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentEncryptionHealthResponse>),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetEncryptionHealth(
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _encryptionService.GetHealthAsync(
            correlationId,
            cancellationToken);

        return result.IsSuccess
            ? Ok(ApiResponse<PaymentEncryptionHealthResponse>.Ok(
                result.Health!,
                correlationId))
            : Failure<PaymentEncryptionHealthResponse>(
                result.FailureKind,
                result.ErrorCode,
                result.ErrorMessage,
                correlationId);
    }

    /// <summary>
    /// Moves this organization's stored ciphertext onto its key ring's active key.
    /// </summary>
    /// <remarks>
    /// Idempotent: a second run over an unchanged organization reports everything skipped. The
    /// old key must still be present in the ring while this runs — it decrypts under the old
    /// key and encrypts under the new one — so remove the old key only once this reports
    /// nothing left to move.
    /// </remarks>
    [HttpPost("encryption/re-encrypt")]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentEncryptionReEncryptionResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentEncryptionReEncryptionResponse>),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ReEncryptPaymentSecrets(
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _encryptionService.ReEncryptAsync(
            correlationId,
            cancellationToken);

        return result.IsSuccess
            ? Ok(ApiResponse<PaymentEncryptionReEncryptionResponse>.Ok(
                result.Summary!,
                correlationId))
            : Failure<PaymentEncryptionReEncryptionResponse>(
                result.FailureKind,
                result.ErrorCode,
                result.ErrorMessage,
                correlationId);
    }

    private IActionResult Failure<TResponse>(
        PaymentFailureKind failureKind,
        string errorCode,
        string errorMessage,
        string correlationId)
    {
        var response = ApiResponse<TResponse>.Fail(
            errorCode,
            errorMessage,
            correlationId);

        return failureKind switch
        {
            PaymentFailureKind.Validation => BadRequest(response),
            PaymentFailureKind.NotFound => NotFound(response),
            PaymentFailureKind.Conflict => Conflict(response),
            PaymentFailureKind.Unavailable => StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                response),
            _ => StatusCode(
                StatusCodes.Status500InternalServerError,
                response)
        };
    }

    private IActionResult Failure(
        PaymentProviderMutationResult result) =>
        Failure(
            result.FailureKind,
            result.ErrorCode,
            result.ErrorMessage,
            result.CorrelationId,
            result.ValidationErrors);

    private IActionResult Failure(
        PaymentFailureKind failureKind,
        string errorCode,
        string errorMessage,
        string correlationId,
        Dictionary<string, string[]>? validationErrors = null)
    {
        var response = ApiResponse<PaymentProviderResponse>.Fail(
            errorCode,
            errorMessage,
            correlationId,
            validationErrors);

        return failureKind switch
        {
            PaymentFailureKind.Validation => BadRequest(response),
            PaymentFailureKind.NotFound => NotFound(response),
            PaymentFailureKind.Conflict => Conflict(response),
            PaymentFailureKind.Unavailable => StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                response),
            _ => StatusCode(
                StatusCodes.Status500InternalServerError,
                response)
        };
    }

    private IActionResult ListFailure(
        PaymentFailureKind failureKind,
        string errorCode,
        string errorMessage,
        string correlationId)
    {
        var response = ApiResponse<
            IReadOnlyList<PaymentProviderResponse>>.Fail(
                errorCode,
                errorMessage,
                correlationId);

        return failureKind switch
        {
            PaymentFailureKind.Unavailable => StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                response),
            _ => StatusCode(
                StatusCodes.Status500InternalServerError,
                response)
        };
    }
}
