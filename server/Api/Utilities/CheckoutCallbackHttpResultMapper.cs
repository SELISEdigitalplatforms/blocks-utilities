using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Enums;
using Payment.DomainService.Responses;

namespace Api.Utilities;

public static class CheckoutCallbackHttpResultMapper
{
    public static IActionResult Map(
        ControllerBase controller,
        CheckoutCallbackResult result)
    {
        if (result.RetryAfterSeconds.HasValue)
        {
            controller.Response.Headers.RetryAfter =
                result.RetryAfterSeconds.Value.ToString();
        }

        if (result.IsRedirect)
        {
            controller.Response.Headers.Location = result.RedirectUrl;
            return controller.StatusCode(StatusCodes.Status303SeeOther);
        }

        var response = ApiResponse<object>.Fail(
            result.ErrorCode,
            result.ErrorMessage,
            controller.HttpContext.TraceIdentifier);

        return result.FailureKind switch
        {
            PaymentFailureKind.NotFound => controller.NotFound(response),
            PaymentFailureKind.RateLimited =>
                controller.StatusCode(StatusCodes.Status429TooManyRequests, response),
            PaymentFailureKind.Unavailable =>
                controller.StatusCode(StatusCodes.Status503ServiceUnavailable, response),
            _ => controller.BadRequest(response)
        };
    }
}
