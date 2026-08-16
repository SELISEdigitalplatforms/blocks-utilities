using Payment.DomainService.Utilities;

namespace Api.Middleware;

/// <summary>
/// Establishes the ambient correlation id for the whole of a request.
/// </summary>
/// <remarks>
/// The controllers already pass <c>HttpContext.TraceIdentifier</c> down as a
/// <c>correlationId</c> argument, and that keeps working. This exists for everything that
/// argument never reached: the log scopes of the services it calls, and the queue commands they
/// dispatch, which is where the trail used to go cold.
/// <para>
/// The same value is echoed as a response header so a caller reporting a problem can quote it
/// even for endpoints that answer with something other than the standard envelope — webhooks
/// and the checkout return among them.
/// </para>
/// </remarks>
public sealed class PaymentCorrelationMiddleware
{
    private const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;

    public PaymentCorrelationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = context.TraceIdentifier;

        // Set before the response starts: headers cannot be added once the body is flushing,
        // and some of these endpoints stream their response.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;

            return Task.CompletedTask;
        });

        using var correlation = PaymentCorrelation.Begin(correlationId);

        await _next(context);
    }
}

public static class PaymentCorrelationMiddlewareExtensions
{
    /// <summary>
    /// Registers correlation before anything that logs, so no log line in a request is written
    /// outside the scope it belongs to.
    /// </summary>
    public static IApplicationBuilder UsePaymentCorrelation(
        this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<PaymentCorrelationMiddleware>();
    }
}
