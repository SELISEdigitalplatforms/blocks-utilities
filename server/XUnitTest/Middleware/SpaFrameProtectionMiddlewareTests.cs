using Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace XUnitTest.Middleware;

public class SpaFrameProtectionMiddlewareTests
{
    [Theory]
    [InlineData("/", "text/html")]
    [InlineData("/index.html", "text/html; charset=utf-8")]
    [InlineData("/app/console", "text/html")]
    [InlineData("/docs/index.html", "TEXT/HTML")]
    public async Task Html_outside_the_api_is_refused_framing(string path, string contentType)
    {
        var headers = await RunAsync(path, contentType);

        Assert.Equal("DENY", headers[SpaFrameProtectionMiddleware.HeaderName].ToString());
    }

    [Theory]
    [InlineData("/api/MagicLink/Invoke/abc", "text/html")]
    [InlineData("/API/subscription-plans", "text/html")]
    [InlineData("/api/subscription-plans", "application/json")]
    [InlineData("/assets/index.js", "text/javascript")]
    [InlineData("/Icon.svg", "image/svg+xml")]
    [InlineData("/health/ready", null)]
    public async Task Api_and_non_html_responses_are_left_alone(string path, string? contentType)
    {
        var headers = await RunAsync(path, contentType);

        Assert.False(headers.ContainsKey(SpaFrameProtectionMiddleware.HeaderName));
    }

    [Fact]
    public async Task The_next_middleware_runs()
    {
        var called = false;
        var middleware = new SpaFrameProtectionMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(new DefaultHttpContext());

        Assert.True(called);
    }

    [Fact]
    public async Task A_null_context_is_rejected()
    {
        var middleware = new SpaFrameProtectionMiddleware(_ => Task.CompletedTask);

        await Assert.ThrowsAsync<ArgumentNullException>(() => middleware.InvokeAsync(null!));
    }

    private static async Task<IHeaderDictionary> RunAsync(string path, string? contentType)
    {
        var feature = new StartableResponseFeature();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(feature);
        context.Request.Path = path;

        var middleware = new SpaFrameProtectionMiddleware(ctx =>
        {
            ctx.Response.ContentType = contentType;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
        await feature.StartAsync();

        return context.Response.Headers;
    }

    /// <summary>A response feature that runs its OnStarting callbacks when told to, as the server does.</summary>
    private sealed class StartableResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _starting = [];

        public override void OnStarting(Func<object, Task> callback, object state) => _starting.Add((callback, state));

        public async Task StartAsync()
        {
            foreach (var (callback, state) in _starting)
            {
                await callback(state);
            }
        }
    }
}
