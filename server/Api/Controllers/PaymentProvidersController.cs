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

    public PaymentProvidersController(
        IPaymentProviderQueryService queryService,
        IPaymentProviderConfigurationService configurationService,
        IPaymentProviderCredentialRotationService credentialRotationService)
    {
        _queryService = queryService;
        _configurationService = configurationService;
        _credentialRotationService = credentialRotationService;
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
