namespace Api.Middleware;

/// <summary>
/// Refuses framing of the console's HTML pages (the SPA shell and the API documentation pages).
/// </summary>
/// <remarks>
/// The console is an authenticated admin application with no page meant to be embedded, so any
/// frame around it is a clickjacking attempt. The header is added to <c>text/html</c> responses
/// outside <c>/api</c> only: API responses are JSON, and the HTML the magic-link endpoint returns
/// is left as it is.
/// </remarks>
public sealed class SpaFrameProtectionMiddleware
{
    /// <summary>The header that refuses framing.</summary>
    public const string HeaderName = "X-Frame-Options";

    /// <summary>Refuse every frame, the console's own included.</summary>
    public const string HeaderValue = "DENY";

    private readonly RequestDelegate _next;

    /// <summary>Creates the middleware around the rest of the pipeline.</summary>
    public SpaFrameProtectionMiddleware(RequestDelegate next) => _next = next;

    /// <summary>Registers the header for the response, then runs the rest of the pipeline.</summary>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Set before the response starts: the content type is only known once the static file or
        // fallback handler has picked the file, and headers cannot be added after the body starts.
        context.Response.OnStarting(() =>
        {
            if (AppliesTo(context.Request.Path, context.Response.ContentType))
            {
                context.Response.Headers[HeaderName] = HeaderValue;
            }

            return Task.CompletedTask;
        });

        return _next(context);
    }

    /// <summary>True for an HTML response outside the API.</summary>
    public static bool AppliesTo(PathString path, string? contentType) =>
        !path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        && contentType is not null
        && contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Registration of <see cref="SpaFrameProtectionMiddleware"/>.</summary>
public static class SpaFrameProtectionMiddlewareExtensions
{
    /// <summary>Adds <see cref="SpaFrameProtectionMiddleware"/>; register it before the static files.</summary>
    public static IApplicationBuilder UseSpaFrameProtection(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<SpaFrameProtectionMiddleware>();
    }
}
