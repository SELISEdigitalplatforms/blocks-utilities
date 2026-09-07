using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using StackExchange.Redis;

namespace XUnitTest.Payment;

public sealed class PaymentRateLimitingAndCacheTests
{
    private static IOptionsMonitor<PaymentOptions> Options(PaymentOptions options)
    {
        var monitor = new Mock<IOptionsMonitor<PaymentOptions>>();
        monitor.SetupGet(m => m.CurrentValue).Returns(options);
        return monitor.Object;
    }

    private static RedisResult TokenBucket(long allowed, long remaining, long retryMs) =>
        RedisResult.Create(new RedisValue[] { allowed, remaining, retryMs });

    private static Mock<IDatabase> DatabaseReturning(RedisResult result)
    {
        var database = new Mock<IDatabase>();
        database.Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(result);
        return database;
    }

    private static ICacheClient Cache(IDatabase database)
    {
        var cache = new Mock<ICacheClient>();
        cache.Setup(client => client.CacheDatabase()).Returns(database);
        return cache.Object;
    }

    private static ICacheClient FailingCache()
    {
        var cache = new Mock<ICacheClient>();
        cache.Setup(client => client.CacheDatabase())
            .Throws(new InvalidOperationException("redis down"));
        return cache.Object;
    }

    // ---- PaymentExecutionContextResolver ----

    [Fact]
    public void Resolver_returns_context_when_authenticated_tenant_present()
    {
        BlocksContext.SetContext(BlocksContext.Create(
            "tenant-1", null, "user-1", true, null, "org-1",
            DateTime.UtcNow.AddHours(1), null, null, null, null, null, null, null));
        try
        {
            var resolution = new PaymentExecutionContextResolver().Resolve("corr-1");

            resolution.IsSuccess.Should().BeTrue();
            resolution.Context!.TenantId.Should().Be("tenant-1");
            resolution.Context.ActorId.Should().Be("user-1");
            resolution.Context.OrganizationId.Should().Be("org-1");
            resolution.Context.UserId.Should().Be("user-1");
        }
        finally
        {
            BlocksContext.ClearContext();
        }
    }

    /// <summary>
    /// The actor falls back to the email so a caller without a user id can still be given a
    /// stable shopper reference. The recorded user id must not inherit that fallback, or the
    /// payments collection would hold email addresses in a field named for an id.
    /// </summary>
    [Fact]
    public void Resolver_does_not_record_an_email_as_the_user_id()
    {
        BlocksContext.SetContext(BlocksContext.Create(
            "tenant-1", null, null, true, null, "org-1",
            DateTime.UtcNow.AddHours(1), "shopper@example.com", null, null, null, null, null,
            null, null));
        try
        {
            var resolution = new PaymentExecutionContextResolver().Resolve("corr-3");

            resolution.IsSuccess.Should().BeTrue();
            resolution.Context!.ActorId.Should().Be("shopper@example.com");
            resolution.Context.UserId.Should().BeNull();
        }
        finally
        {
            BlocksContext.ClearContext();
        }
    }

    [Fact]
    public void Resolver_returns_failure_when_tenant_context_missing()
    {
        BlocksContext.ClearContext();

        var resolution = new PaymentExecutionContextResolver().Resolve("corr-2");

        resolution.IsSuccess.Should().BeFalse();
        resolution.Failure!.ErrorCode.Should().Be("payment_context_missing");
    }

    /// <summary>
    /// The shape of a client-credentials caller: a tenant and an organization it proved, an
    /// application id, and no person anywhere.
    /// </summary>
    private static void MachineCaller(string? clientId, string? oauthToken = null) =>
        BlocksContext.SetContext(BlocksContext.Create(
            "tenant-1", null, null, true, null, "org-1",
            DateTime.UtcNow.AddHours(1), null, null, null, null, null,
            oauthToken, "tenant-1", clientId: clientId));

    /// <summary>
    /// An application authenticates as itself, so the context names no user and no email. It is
    /// named by what it is instead of being refused for not being a person.
    /// </summary>
    [Fact]
    public void Resolver_names_a_machine_caller_by_its_client_id()
    {
        MachineCaller("app-1");
        try
        {
            var resolution = new PaymentExecutionContextResolver().Resolve("corr-4");

            resolution.IsSuccess.Should().BeTrue();
            resolution.Context!.ActorId.Should().Be("client:app-1");
        }
        finally
        {
            BlocksContext.ClearContext();
        }
    }

    /// <summary>
    /// The same rule that keeps an email out of the user id: a fallback names the caller without
    /// passing itself off as the thing it stood in for.
    /// </summary>
    [Fact]
    public void Resolver_does_not_record_a_client_id_as_the_user_id()
    {
        MachineCaller("app-1");
        try
        {
            new PaymentExecutionContextResolver().Resolve("corr-5")
                .Context!.UserId.Should().BeNull();
        }
        finally
        {
            BlocksContext.ClearContext();
        }
    }

    /// <summary>
    /// The client id sits below the person, not in front of one. A caller the context already
    /// names is named by the context, or the same request would be attributed differently
    /// depending on which rung happened to be consulted.
    /// </summary>
    [Fact]
    public void A_caller_the_context_names_outranks_its_client_id()
    {
        BlocksContext.SetContext(BlocksContext.Create(
            "tenant-1", null, "user-1", true, null, "org-1",
            DateTime.UtcNow.AddHours(1), null, null, null, null, null,
            null, "tenant-1", clientId: "app-1"));
        try
        {
            new PaymentExecutionContextResolver().Resolve("corr-6")
                .Context!.ActorId.Should().Be("user-1");
        }
        finally
        {
            BlocksContext.ClearContext();
        }
    }

    /// <summary>
    /// An application named identically whichever rung supplied it, so one caller cannot read as
    /// two actors in an audit trail.
    /// </summary>
    [Fact]
    public void Both_client_id_rungs_spell_the_actor_the_same_way()
    {
        // "app-1" as the client_id claim of a signed token, for the rung below.
        const string Token = "header.eyJjbGllbnRfaWQiOiJhcHAtMSJ9.signature";

        MachineCaller(clientId: null, oauthToken: Token);
        string? fromToken;
        try
        {
            fromToken = new PaymentExecutionContextResolver().Resolve("corr-7").Context?.ActorId;
        }
        finally
        {
            BlocksContext.ClearContext();
        }

        MachineCaller("app-1");
        try
        {
            new PaymentExecutionContextResolver().Resolve("corr-8")
                .Context!.ActorId.Should().Be(fromToken);
        }
        finally
        {
            BlocksContext.ClearContext();
        }
    }

    // ---- PaymentIdempotencyCache ----

    private static PaymentIdempotencyCache IdempotencyCache(ICacheClient cache) =>
        new(cache, NullLogger<PaymentIdempotencyCache>.Instance);

    [Fact]
    public async Task Idempotency_get_returns_stored_payment_id()
    {
        var database = new Mock<IDatabase>();
        database.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync("payment-9");

        var result = await IdempotencyCache(Cache(database.Object))
            .GetPaymentIdAsync("tenant-1", "key-1", CancellationToken.None);

        result.Should().Be("payment-9");
    }

    [Fact]
    public async Task Idempotency_get_returns_null_when_absent()
    {
        var database = new Mock<IDatabase>();
        database.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var result = await IdempotencyCache(Cache(database.Object))
            .GetPaymentIdAsync("tenant-1", "key-1", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Idempotency_get_returns_null_when_cache_unavailable()
    {
        var result = await IdempotencyCache(FailingCache())
            .GetPaymentIdAsync("tenant-1", "key-1", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Idempotency_get_honours_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => IdempotencyCache(FailingCache())
            .GetPaymentIdAsync("tenant-1", "key-1", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Idempotency_set_writes_value_through_cache_database()
    {
        var database = new Mock<IDatabase>();
        var cache = new Mock<ICacheClient>();
        cache.Setup(client => client.CacheDatabase()).Returns(database.Object);

        var act = () => IdempotencyCache(cache.Object)
            .SetPaymentIdAsync("tenant-1", "key-1", "payment-9", CancellationToken.None);

        await act.Should().NotThrowAsync();
        cache.Verify(client => client.CacheDatabase(), Times.Once);
    }

    [Fact]
    public async Task Idempotency_set_honours_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => IdempotencyCache(FailingCache())
            .SetPaymentIdAsync("tenant-1", "key-1", "payment-9", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Idempotency_set_swallows_cache_failures()
    {
        var act = () => IdempotencyCache(FailingCache())
            .SetPaymentIdAsync("tenant-1", "key-1", "payment-9", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    // ---- PaymentRateLimiter ----

    private static PaymentRateLimiter PaymentLimiter(ICacheClient cache) =>
        new(cache, Options(new PaymentOptions
        {
            TenantRequestsPerMinute = 100,
            ActorRequestsPerMinute = 50,
            OrderRequestsPerMinute = 10
        }), NullLogger<PaymentRateLimiter>.Instance);

    [Fact]
    public async Task Payment_rate_limiter_allows_when_all_rules_have_tokens()
    {
        var result = await PaymentLimiter(Cache(DatabaseReturning(TokenBucket(1, 5, 0)).Object))
            .CheckAsync("tenant-1", "actor-1", "order-1", CancellationToken.None);

        result.IsAllowed.Should().BeTrue();
        result.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task Payment_rate_limiter_blocks_and_reports_retry_when_exhausted()
    {
        var result = await PaymentLimiter(Cache(DatabaseReturning(TokenBucket(0, 0, 4000)).Object))
            .CheckAsync("tenant-1", "actor-1", "order-1", CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.RetryAfterSeconds.Should().Be(4);
    }

    [Fact]
    public async Task Payment_rate_limiter_fails_closed_when_cache_unavailable()
    {
        var result = await PaymentLimiter(FailingCache())
            .CheckAsync("tenant-1", "actor-1", "order-1", CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.IsAvailable.Should().BeFalse();
        result.RetryAfterSeconds.Should().Be(30);
    }

    [Fact]
    public async Task Payment_rate_limiter_fails_closed_on_malformed_response()
    {
        var database = DatabaseReturning(RedisResult.Create(new RedisValue[] { 1, 5 }));

        var result = await PaymentLimiter(Cache(database.Object))
            .CheckAsync("tenant-1", "actor-1", "order-1", CancellationToken.None);

        result.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Payment_rate_limiter_honours_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => PaymentLimiter(FailingCache())
            .CheckAsync("tenant-1", "actor-1", "order-1", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---- CheckoutCallbackRateLimiter ----

    private static CheckoutCallbackRateLimiter CallbackLimiter(ICacheClient cache) =>
        new(cache, Options(new PaymentOptions
        {
            ReturnRequestsPerClientPerMinute = 20,
            ReturnRequestsPerStatePerMinute = 5
        }), NullLogger<CheckoutCallbackRateLimiter>.Instance);

    [Fact]
    public async Task Callback_rate_limiter_allows_when_tokens_available()
    {
        var result = await CallbackLimiter(Cache(DatabaseReturning(TokenBucket(1, 3, 0)).Object))
            .CheckAsync("203.0.113.5", "signed-state", CancellationToken.None);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task Callback_rate_limiter_blocks_when_first_rule_exhausted()
    {
        var result = await CallbackLimiter(Cache(DatabaseReturning(TokenBucket(0, 0, 2000)).Object))
            .CheckAsync("203.0.113.5", "signed-state", CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.RetryAfterSeconds.Should().Be(2);
    }

    [Fact]
    public async Task Callback_rate_limiter_fails_closed_when_cache_unavailable()
    {
        var result = await CallbackLimiter(FailingCache())
            .CheckAsync("203.0.113.5", "signed-state", CancellationToken.None);

        result.IsAvailable.Should().BeFalse();
        result.IsAllowed.Should().BeFalse();
    }

    // ---- StoredPaymentMethodRateLimiter ----

    private static StoredPaymentMethodRateLimiter MethodLimiter(ICacheClient cache) =>
        new(cache, Options(new PaymentOptions
        {
            StoredPaymentMethodListRequestsPerMinute = 30,
            StoredPaymentMethodRemovalRequestsPerMinute = 6
        }), NullLogger<StoredPaymentMethodRateLimiter>.Instance);

    [Fact]
    public async Task Method_rate_limiter_allows_list_when_tokens_available()
    {
        var result = await MethodLimiter(Cache(DatabaseReturning(TokenBucket(1, 9, 0)).Object))
            .CheckListAsync("tenant-1", "actor-1", CancellationToken.None);

        result.IsAllowed.Should().BeTrue();
        result.Limit.Should().Be(30);
    }

    [Fact]
    public async Task Method_rate_limiter_blocks_removal_when_exhausted()
    {
        var result = await MethodLimiter(Cache(DatabaseReturning(TokenBucket(0, 0, 5000)).Object))
            .CheckRemovalAsync("tenant-1", "actor-1", CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.RetryAfterSeconds.Should().Be(5);
    }

    [Fact]
    public async Task Method_rate_limiter_fails_closed_on_malformed_response()
    {
        var database = DatabaseReturning(RedisResult.Create(new RedisValue[] { 1 }));

        var result = await MethodLimiter(Cache(database.Object))
            .CheckListAsync("tenant-1", "actor-1", CancellationToken.None);

        result.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Method_rate_limiter_honours_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => MethodLimiter(FailingCache())
            .CheckRemovalAsync("tenant-1", "actor-1", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
