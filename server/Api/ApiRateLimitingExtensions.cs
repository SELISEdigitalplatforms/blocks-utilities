using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Blocks.Genesis;
using Microsoft.AspNetCore.RateLimiting;

namespace BlocksTemplate.Api
{
    public static class ApiRateLimitingExtensions
    {
        private const string MailSendPolicyName = "mail-send-api";
        private const string GeneralPolicyName = "general-api";

        public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
        {
            if (!configuration.GetValue("ApiRateLimiting:Enabled", true))
            {
                return services;
            }

            services.AddRateLimiter(options =>
            {
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                {
                    var policy = GetPolicy(context.Request);
                    var partition = GetPartitionKey(context);
                    var limits = GetLimits(configuration, policy);

                    return RateLimitPartition.GetFixedWindowLimiter(
                        $"{policy}:{partition.KeyType}:{partition.Value}",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            AutoReplenishment = true,
                            PermitLimit = limits.PermitLimit,
                            QueueLimit = limits.QueueLimit,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            Window = TimeSpan.FromSeconds(limits.WindowSeconds)
                        });
                });

                options.OnRejected = async (context, cancellationToken) =>
                {
                    var retryAfterSeconds = GetRetryAfterSeconds(context.Lease);
                    var partition = GetPartitionKey(context.HttpContext);
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("ApiRateLimiting");

                    if (retryAfterSeconds.HasValue)
                    {
                        context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.Value.ToString();
                    }

                    logger.LogWarning(
                        "API rate limit exceeded. Method={Method}, Path={Path}, PartitionKeyType={PartitionKeyType}, PartitionKey={PartitionKey}, RetryAfterSeconds={RetryAfterSeconds}, RemoteIp={RemoteIp}",
                        context.HttpContext.Request.Method,
                        context.HttpContext.Request.Path.Value,
                        partition.KeyType,
                        partition.Value,
                        retryAfterSeconds,
                        context.HttpContext.Connection.RemoteIpAddress?.ToString());

                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.HttpContext.Response.ContentType = "application/json";

                    var payload = new
                    {
                        isSuccess = false,
                        errors = new Dictionary<string, string>
                        {
                            { "RateLimit", "API rate limit exceeded. Retry later." }
                        },
                        retryAfterSeconds
                    };

                    await JsonSerializer.SerializeAsync(
                        context.HttpContext.Response.Body,
                        payload,
                        cancellationToken: cancellationToken);
                };
            });

            return services;
        }

        public static IApplicationBuilder UseApiRateLimiting(this IApplicationBuilder app, IConfiguration configuration)
        {
            return configuration.GetValue("ApiRateLimiting:Enabled", true)
                ? app.UseRateLimiter()
                : app;
        }

        public static ApiRateLimitPartition GetPartitionKey(HttpContext context)
        {
            var blocksContext = BlocksContext.GetContext();
            if (!string.IsNullOrWhiteSpace(blocksContext?.TenantId))
            {
                return new ApiRateLimitPartition("tenant", Normalize(blocksContext.TenantId));
            }

            var userId = GetClaimValue(context.User, ClaimTypes.NameIdentifier)
                ?? GetClaimValue(context.User, "sub")
                ?? GetClaimValue(context.User, "client_id")
                ?? GetClaimValue(context.User, "azp");

            if (!string.IsNullOrWhiteSpace(userId))
            {
                return new ApiRateLimitPartition("principal", Normalize(userId));
            }

            var blocksKey = GetHeaderValue(context, "x-blocks-key");
            if (!string.IsNullOrWhiteSpace(blocksKey))
            {
                return new ApiRateLimitPartition("blocks-key", Normalize(blocksKey));
            }

            var projectKey = GetHeaderValue(context, "project-key") ?? GetHeaderValue(context, "x-project-key");
            if (!string.IsNullOrWhiteSpace(projectKey))
            {
                return new ApiRateLimitPartition("project-key", Normalize(projectKey));
            }

            var remoteIp = context.Connection.RemoteIpAddress?.ToString();
            return new ApiRateLimitPartition("ip", Normalize(remoteIp ?? "unknown"));
        }

        public static string GetPolicy(HttpRequest request)
        {
            if (!HttpMethods.IsPost(request.Method))
            {
                return GeneralPolicyName;
            }

            var path = request.Path.Value ?? string.Empty;
            return path.Equals("/api/Mail/Send", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/api/Mail/SendToAny", StringComparison.OrdinalIgnoreCase)
                    ? MailSendPolicyName
                    : GeneralPolicyName;
        }

        private static ApiRateLimitSettings GetLimits(IConfiguration configuration, string policy)
        {
            if (string.Equals(policy, MailSendPolicyName, StringComparison.Ordinal))
            {
                return new ApiRateLimitSettings(
                    Math.Max(1, configuration.GetValue<int?>("ApiRateLimiting:MailSendPermitLimit") ?? 60),
                    Math.Max(1, configuration.GetValue<int?>("ApiRateLimiting:MailSendWindowSeconds") ?? 60),
                    Math.Max(0, configuration.GetValue<int?>("ApiRateLimiting:MailSendQueueLimit") ?? 0));
            }

            return new ApiRateLimitSettings(
                Math.Max(1, configuration.GetValue<int?>("ApiRateLimiting:GeneralPermitLimit") ?? 300),
                Math.Max(1, configuration.GetValue<int?>("ApiRateLimiting:GeneralWindowSeconds") ?? 60),
                Math.Max(0, configuration.GetValue<int?>("ApiRateLimiting:GeneralQueueLimit") ?? 0));
        }

        private static int? GetRetryAfterSeconds(RateLimitLease lease)
        {
            return lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                : null;
        }

        private static string? GetClaimValue(ClaimsPrincipal principal, string claimType)
        {
            return principal.FindFirst(claimType)?.Value;
        }

        private static string? GetHeaderValue(HttpContext context, string headerName)
        {
            return context.Request.Headers.TryGetValue(headerName, out var value)
                ? value.FirstOrDefault()
                : null;
        }

        private static string Normalize(string value)
        {
            return value.Trim().ToLowerInvariant();
        }

        public sealed record ApiRateLimitPartition(string KeyType, string Value);
        private sealed record ApiRateLimitSettings(int PermitLimit, int WindowSeconds, int QueueLimit);
    }
}
