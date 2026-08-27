using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;
using Payment.DomainService.Services;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Integration;

/// <summary>
/// A captured payment becoming an invoice, through the queue, end to end.
/// </summary>
/// <remarks>
/// The queue repository tests prove scheduling, occurrence uniqueness, atomic claiming and leases.
/// None of that was what failed in production: the queue held valid pending items and nothing turned
/// them into tenant financial documents. A green repository suite cannot see a missing handler
/// registration, a work type dispatched to the wrong handler, a tenant context that never got
/// established, or a document written into the scheduling database.
/// <para>
/// So this runs the real announcer, the real queue, the real dispatcher and the real handlers, with
/// two <strong>physically separate</strong> databases standing in for the tenant's and for
/// <c>BlocksRootDb</c>. One shared database would let a financial document written into the root pass
/// unnoticed, which is one of the failures worth catching.
/// </para>
/// <para>
/// Only the leaves outside this module are controlled: the PDF renderer, the file store and the
/// message client. Everything between the announcement and the stored document is the production
/// code path.
/// </para>
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class SubscriptionQueueDocumentFlowIntegrationTests
{
    private const string OrganizationId = "org-1";

    private readonly MongoIntegrationFixture _fixture;
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));

    private readonly SubscriptionRepository _subscriptions;
    private readonly SubscriptionFinancialDocumentRepository _documents;
    private readonly SubscriptionBillingProfileRepository _profiles;
    private readonly PaymentRepository _payments;
    private readonly SubscriptionWorkQueue _queue;
    private readonly SubscriptionWorkScheduler _scheduler;

    private readonly RecordingPdfRenderer _renderer = new();
    private readonly RecordingFileStore _files = new();
    private readonly RecordingMessageClient _messages = new();

    public SubscriptionQueueDocumentFlowIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;

        // Tenant-scoped repositories resolve to the tenant database; the queue below asks for the
        // root database by name and gets a different one.
        var provider = fixture.SplitDbContextProvider;

        _subscriptions = new SubscriptionRepository(provider);
        _documents = new SubscriptionFinancialDocumentRepository(provider);
        _profiles = new SubscriptionBillingProfileRepository(provider);
        _payments = new PaymentRepository(
            provider,
            new StaticPaymentOptions(new PaymentOptions()));

        var secret = new Mock<IBlocksSecret>();
        secret.SetupGet(value => value.DatabaseConnectionString)
            .Returns(MongoIntegrationFixture.ConnectionString);
        secret.SetupGet(value => value.RootDatabaseName).Returns(fixture.RootDatabaseName);

        _queue = new SubscriptionWorkQueue(provider, secret.Object, _time);
        _scheduler = new SubscriptionWorkScheduler(
            _queue,
            new StaticOptionsMonitor(new SubscriptionOptions()),
            NullLogger<SubscriptionWorkScheduler>.Instance,
            _time);
    }

    /// <summary>
    /// The whole chain: captured payment, announcement, claim, document, delivery.
    /// </summary>
    [Fact]
    public async Task A_captured_payment_becomes_an_invoice_in_the_tenant_database_through_the_queue()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var paymentId = "pay-" + Guid.NewGuid().ToString("N");
        var subscription = await SeedAsync(tenantId, paymentId);

        // 1. Announce, exactly as the production charge path does.
        await Announcer(tenantId).AnnounceChargeAsync(
            subscription,
            paymentId,
            SubscriptionChargeKind.Renewal,
            "2026-08",
            "corr-charge",
            CancellationToken.None);

        // 2. One issue job, in the root database and nowhere else.
        var issueJobs = await PendingAsync(SubscriptionWorkType.FinancialDocumentIssue);

        issueJobs.Should().ContainSingle();
        issueJobs[0].TenantId.Should().Be(tenantId);
        issueJobs[0].WorkKey.Should().Be($"payment:{paymentId}");

        // 3. Drain it with the real dispatcher and the real handler set.
        var processed = await Dispatcher(tenantId).ProcessDueAsync("it-worker", CancellationToken.None);

        processed.Should().BeGreaterThan(0);

        // Asserted before the document, so a handler that failed says why instead of leaving an
        // empty collection to be explained. A silent "no document" is the least useful failure this
        // test could produce, given it exists to catch wiring that does not run.
        await AssertWorkCompletedAsync(SubscriptionWorkType.FinancialDocumentIssue);

        // 4. The document is in the tenant's database. Read from the collection rather than through
        // the repository, because the question here is which database it landed in.
        var stored = await TenantDocumentsAsync(tenantId);

        stored.Should().ContainSingle();

        var invoice = stored[0];

        invoice.OrganizationId.Should().Be(OrganizationId);
        invoice.SubscriptionId.Should().Be(subscription.ItemId);
        invoice.DocumentNumber.Should().NotBeNullOrWhiteSpace();
        invoice.Amounts.TotalMinor.Should().Be(2_500);
        invoice.BillingContact.Email.Should().Be("ada@northwind.example");
        invoice.Subscriber.LegalName.Should().Be("Northwind Trading AG");

        // 5. And nowhere near the scheduling database. This is the assertion a single-database
        // harness could not make, and the failure it could not have seen.
        (await _fixture.RootDatabase
                .GetCollection<BsonDocument>("SubscriptionFinancialDocuments")
                .CountDocumentsAsync(new BsonDocument(), cancellationToken: CancellationToken.None))
            .Should().Be(0, "financial documents belong to the tenant, not to BlocksRootDb");

        // 6. Issuing announces delivery. Without this the invoice exists and never reaches anybody.
        var deliveryJobs = await PendingAsync(SubscriptionWorkType.FinancialDocumentDelivery);

        deliveryJobs.Should().NotBeEmpty();

        // 7. Deliver it, through the same dispatcher.
        _time.Advance(TimeSpan.FromSeconds(1));
        await Dispatcher(tenantId).ProcessDueAsync("it-worker", CancellationToken.None);

        var delivered = await _documents.GetAsync(
            tenantId, invoice.ItemId, CancellationToken.None);

        delivered!.Delivery.StorageId.Should().NotBeNullOrWhiteSpace();
        delivered.Delivery.State.Should().BeOneOf(
            FinancialDocumentDeliveryState.Generated,
            FinancialDocumentDeliveryState.Delivered);

        _renderer.Renders.Should().Be(1);
        _files.Saved.Should().Be(1);
        _messages.Sent.Should().Be(1);
    }

    /// <summary>
    /// Replaying the same work charges nothing twice and issues nothing twice.
    /// </summary>
    /// <remarks>
    /// The queue's own uniqueness stops two items for one occurrence. This asks the harder question:
    /// if the same announcement and the same drain happen again @D@ a retry, a repair sweep covering
    /// for a producer, a redelivered message @D@ does the tenant end up with one invoice or two.
    /// </remarks>
    [Fact]
    public async Task Replaying_the_whole_flow_produces_no_second_invoice_number_pdf_or_email()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var paymentId = "pay-" + Guid.NewGuid().ToString("N");
        var subscription = await SeedAsync(tenantId, paymentId);
        var announcer = Announcer(tenantId);

        await announcer.AnnounceChargeAsync(
            subscription, paymentId, SubscriptionChargeKind.Renewal, "2026-08", "corr-1",
            CancellationToken.None);

        await Dispatcher(tenantId).ProcessDueAsync("worker-a", CancellationToken.None);
        await AssertWorkCompletedAsync(SubscriptionWorkType.FinancialDocumentIssue);

        _time.Advance(TimeSpan.FromSeconds(1));
        await Dispatcher(tenantId).ProcessDueAsync("worker-a", CancellationToken.None);

        var first = await TenantDocumentsAsync(tenantId);

        first.Should().ContainSingle();

        var number = first[0].DocumentNumber;

        // The same announcement again, from a producer that retried or a sweep that found the
        // obligation still recorded.
        _time.Advance(TimeSpan.FromMinutes(1));
        await announcer.AnnounceChargeAsync(
            subscription, paymentId, SubscriptionChargeKind.Renewal, "2026-08", "corr-2",
            CancellationToken.None);

        await Dispatcher(tenantId).ProcessDueAsync("worker-b", CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(1));
        await Dispatcher(tenantId).ProcessDueAsync("worker-b", CancellationToken.None);

        var second = await TenantDocumentsAsync(tenantId);

        // One document, one number. Two would be two invoices for one payment, which cannot be
        // repaired afterwards because both have been sent.
        second.Should().ContainSingle();
        second[0].DocumentNumber.Should().Be(number);

        // And nothing was rendered or emailed a second time.
        _renderer.Renders.Should().Be(1);
        _messages.Sent.Should().Be(1);
    }

    // ------------------------------------------------------------------------------ arrangement

    /// <summary>
    /// A tenant with everything issuing an invoice needs, and a captured payment.
    /// </summary>
    private async Task<SubscriptionDetail> SeedAsync(string tenantId, string paymentId)
    {
        await _profiles.UpsertAsync(
            new SubscriptionBillingProfile
            {
                TenantId = tenantId,
                OrganizationId = OrganizationId,
                LegalName = "Northwind Trading AG",
                BillingContactName = "Ada Byron",
                BillingContactEmail = "ada@northwind.example"
            },
            CancellationToken.None);

        var subscription = new SubscriptionDetail
        {
            ItemId = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            OrganizationId = OrganizationId,
            Status = SubscriptionStatus.Active,
            CurrencyCode = "CHF",
            CurrentPeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            Plan = new PlanSnapshot { Code = "professional", DisplayName = "Professional" },
            Price = new PriceSnapshot
            {
                PriceId = "price-1",
                CurrencyCode = "CHF",
                UnitAmountMinor = 2_500,
                Interval = BillingInterval.Month,
                IntervalCount = 1
            }
        };

        (await _subscriptions.TryCreateAsync(subscription, CancellationToken.None))
            .Should().BeTrue("the seed must exist before anything is announced");

        (await _payments.TryCreateAsync(
                new PaymentDetail
                {
                    ItemId = paymentId,
                    TenantId = tenantId,
                    OrganizationId = OrganizationId,
                    CustomerOrganizationId = OrganizationId,
                    Amount = 25,
                    CurrencyCode = "CHF",
                    PaymentStatus = PaymentStatuses.Captured,
                    CreatedAtUtc = _time.GetUtcNow().UtcDateTime
                },
                CancellationToken.None))
            .Should().BeTrue(
                "the captured payment is what the invoice describes. ItemId is the collection's _id, " +
                "so it has to be unique across tenants and not only within one");

        return subscription;
    }

    private SubscriptionFinancialDocumentAnnouncer Announcer(string tenantId) => new(
        _scheduler,
        _subscriptions,
        NullLogger<SubscriptionFinancialDocumentAnnouncer>.Instance,
        _time);

    /// <summary>
    /// The real dispatcher, with the real handler set resolved from a container.
    /// </summary>
    /// <remarks>
    /// A container rather than hand-built handlers on purpose: a work type whose handler is not
    /// registered is one of the failures a repository test cannot see, and it can only be caught by
    /// resolving them the way the worker does.
    /// </remarks>
    private SubscriptionWorkDispatcher Dispatcher(string tenantId)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton(Options.Create(new SubscriptionOptions()));
        services.AddSingleton<ISubscriptionRepository>(_subscriptions);
        services.AddSingleton<ISubscriptionFinancialDocumentRepository>(_documents);
        services.AddSingleton<ISubscriptionBillingProfileRepository>(_profiles);
        services.AddSingleton<IPaymentRepository>(_payments);
        services.AddSingleton<ISubscriptionWorkScheduler>(_scheduler);
        services.AddSingleton<IFinancialDocumentNumberAllocator>(
            new FinancialDocumentNumberAllocator(_fixture.SplitDbContextProvider));
        services.AddSingleton<ISubscriptionInvoiceHistoryRepository>(
            new SubscriptionInvoiceHistoryRepository(_fixture.SplitDbContextProvider, _payments));
        services.AddSingleton<ISubscriptionDocumentCursorRepository>(
            new SubscriptionDocumentCursorRepository(_fixture.SplitDbContextProvider));
        services.AddSingleton<ICurrencyMinorUnitResolver, CurrencyMinorUnitResolver>();
        services.AddSingleton<ISubscriptionMerchantProfileService, FixedMerchantProfile>();
        services.AddSingleton<IFinancialDocumentPdfRenderer>(_renderer);
        services.AddSingleton<IFinancialDocumentFileStore>(_files);
        services.AddSingleton(_messages.Client.Object);
        services.AddSingleton<IPaymentTenantContextScopeFactory, NoOpTenantScopeFactory>();

        services.AddSingleton<
            ISubscriptionFinancialDocumentIssuer, SubscriptionFinancialDocumentIssuer>();
        services.AddSingleton<
            ISubscriptionFinancialDocumentDeliveryService,
            SubscriptionFinancialDocumentDeliveryService>();

        services.AddSingleton<ISubscriptionWorkHandler, FinancialDocumentIssueWorkHandler>();
        services.AddSingleton<ISubscriptionWorkHandler, FinancialDocumentDeliveryWorkHandler>();

        var built = services.BuildServiceProvider();

        return new SubscriptionWorkDispatcher(
            _queue,
            built.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor(new SubscriptionOptions()),
            NullLogger<SubscriptionWorkDispatcher>.Instance,
            _time,
            leaseOverride: TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Fails with the handler's own error when a claimed item did not complete.
    /// </summary>
    /// <remarks>
    /// Without this the symptom is an empty document collection, which is indistinguishable from a
    /// producer that never announced anything — and this test exists precisely to tell those apart.
    /// The queue records the error code and message a handler failed with, so the assertion can say
    /// it out loud.
    /// </remarks>
    private async Task AssertWorkCompletedAsync(SubscriptionWorkType workType)
    {
        var items = await PendingAsync(workType);

        items.Should().NotBeEmpty($"{workType} should have been announced");

        var unfinished = items
            .Where(item => item.Status != global::Subscription.DomainService.Enums.BackgroundWorkStatus.Completed)
            .ToList();

        unfinished.Should().BeEmpty(
            "the queue is the only executor, so an item that did not complete is work that never " +
            "happened. Outcomes: " +
            string.Join(
                "; ",
                items.Select(item =>
                    $"{item.WorkType} status={item.Status} attempts={item.AttemptCount} " +
                    $"error={item.LastErrorCode ?? "none"} message={item.LastErrorMessage ?? "none"}")));
    }

    private async Task<IReadOnlyList<SubscriptionBackgroundWork>> PendingAsync(
        SubscriptionWorkType workType) =>
        await _fixture.RootDatabase
            .GetCollection<SubscriptionBackgroundWork>("SubscriptionBackgroundWork")
            .Find(Builders<SubscriptionBackgroundWork>.Filter.Eq(
                work => work.WorkType,
                workType))
            .ToListAsync(CancellationToken.None);

    /// <summary>
    /// Every financial document in the tenant's own database.
    /// </summary>
    /// <remarks>
    /// Read from the collection rather than through the repository's list, which takes an
    /// organization and a page and answers "what would a client see". The question here is narrower
    /// and more important: what exists, and in which database.
    /// </remarks>
    private async Task<IReadOnlyList<SubscriptionFinancialDocument>> TenantDocumentsAsync(
        string tenantId) =>
        await _fixture.Database
            .GetCollection<SubscriptionFinancialDocument>("SubscriptionFinancialDocuments")
            .Find(Builders<SubscriptionFinancialDocument>.Filter.Eq(
                document => document.TenantId,
                tenantId))
            .ToListAsync(CancellationToken.None);

    // ------------------------------------------------------------------------------ controlled leaves

    /// <summary>A seller, without the console gate and the validator behind the real service.</summary>
    /// <remarks>
    /// Only <see cref="ResolveAsync"/> and <see cref="MissingFieldsAsync"/> are part of this flow. The
    /// read and write endpoints throw rather than returning something plausible, so a change that
    /// starts calling them here fails loudly instead of quietly issuing under a stand-in.
    /// </remarks>
    private sealed class FixedMerchantProfile : ISubscriptionMerchantProfileService
    {
        public Task<SubscriptionOperationResult<SubscriptionMerchantProfileResponse>> GetAsync(
            string correlationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("not part of the document flow");

        public Task<SubscriptionOperationResult<SubscriptionMerchantProfileResponse>> UpdateAsync(
            UpdateMerchantProfileRequest request,
            string correlationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("not part of the document flow");

        public Task<FinancialDocumentMerchant> ResolveAsync(
            string tenantId, CancellationToken cancellationToken) =>
            Task.FromResult(new FinancialDocumentMerchant { LegalName = "Blocks Platform AG" });

        public Task<IReadOnlyList<string>> MissingFieldsAsync(
            string tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    /// <summary>Counts renders. A headless browser is not what this test is about.</summary>
    private sealed class RecordingPdfRenderer : IFinancialDocumentPdfRenderer
    {
        private int _renders;

        public int Renders => Volatile.Read(ref _renders);

        public Task<byte[]?> RenderAsync(string html, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _renders);

            return Task.FromResult<byte[]?>([1, 2, 3, 4]);
        }
    }

    private sealed class RecordingFileStore : IFinancialDocumentFileStore
    {
        private int _saved;

        public int Saved => Volatile.Read(ref _saved);

        public Task<bool> SaveAsync(
            string storageId,
            string fileName,
            byte[] content,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _saved);

            return Task.FromResult(true);
        }

        public Task<byte[]?> ReadAsync(string storageId, CancellationToken cancellationToken) =>
            Task.FromResult<byte[]?>([1, 2, 3, 4]);
    }

    /// <summary>
    /// Counts what was sent, through Moq rather than a hand-written double.
    /// </summary>
    /// <remarks>
    /// The interface's send methods are generic with their own constraints, and restating them by hand
    /// is a way to get a double that compiles against a shape the real client does not have. Moq
    /// derives them from the interface itself.
    /// </remarks>
    private sealed class RecordingMessageClient
    {
        private int _sent;

        public RecordingMessageClient()
        {
            var mock = new Mock<IMessageClient>();

            mock
                .Setup(client => client.SendToConsumerAsync(It.IsAny<ConsumerMessage<object>>()))
                .Callback(() => Interlocked.Increment(ref _sent))
                .Returns(Task.CompletedTask);

            Client = mock;
        }

        public Mock<IMessageClient> Client { get; }

        /// <summary>
        /// How many sends the client was asked for, counted from its own invocation list.
        /// </summary>
        /// <remarks>
        /// Read from Moq rather than from a callback, because the message's own generic argument is an
        /// internal detail of the delivery service and a `Setup` naming the wrong one would silently
        /// count nothing.
        /// </remarks>
        public int Sent => Client.Invocations.Count(invocation =>
            invocation.Method.Name is nameof(IMessageClient.SendToConsumerAsync)
                or nameof(IMessageClient.SendToMassConsumerAsync));
    }

    /// <summary>Background work has no request, so there is no ambient tenant to establish here.</summary>
    private sealed class NoOpTenantScopeFactory : IPaymentTenantContextScopeFactory
    {
        public IDisposable Establish(string tenantId) => new Nothing();

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class StaticPaymentOptions : IOptionsMonitor<PaymentOptions>
    {
        public StaticPaymentOptions(PaymentOptions value) => CurrentValue = value;

        public PaymentOptions CurrentValue { get; }

        public PaymentOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<PaymentOptions, string?> listener) => new Nothing();

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class StaticOptionsMonitor : IOptionsMonitor<SubscriptionOptions>
    {
        public StaticOptionsMonitor(SubscriptionOptions value) => CurrentValue = value;

        public SubscriptionOptions CurrentValue { get; }

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<SubscriptionOptions, string?> listener) =>
            new Nothing();

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
