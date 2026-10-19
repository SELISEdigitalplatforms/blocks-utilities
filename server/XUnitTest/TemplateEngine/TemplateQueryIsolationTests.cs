using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Utility.DomainService.Shared.Utilities;
using Utility.DomainService.TemplateEngine;
using Utility.DomainService.TemplateEngine.Events;
using Utility.DomainService.TemplateEngine.service;

namespace XUnitTest.TemplateEngine;

/// <summary>
/// The template engine runs caller-written Mongo queries against a tenant database and renders
/// the results into a file the caller can download.
/// </summary>
/// <remarks>
/// Guards against data exfiltration through that file. Any signed-in user could name another
/// tenant in <c>ProjectKey</c> and read its database, and any user could read platform
/// collections of their own tenant: every OAuth client secret in <c>ClientCredentials</c>, IAM
/// users, payment and subscription records. Operators that run JavaScript on the database
/// server are refused for the same reason.
/// </remarks>
public sealed class TemplateQueryIsolationTests : IDisposable
{
    private readonly Mock<IDbContextProvider> _databases = new();
    private readonly Mock<IMessageClient> _messages = new();
    private readonly MongoQueryHelper _queries;
    private readonly TemplateEngineService _service;

    public TemplateQueryIsolationTests()
    {
        BlocksContext.SetContext(BlocksContext.Create(
            "tenant-a", null, "user-1", true, null, "org-1",
            DateTime.UtcNow.AddHours(1), null, null, null, null, null, null, null));

        _queries = new MongoQueryHelper(Mock.Of<ILogger<MongoQueryHelper>>(), _databases.Object);
        _service = new TemplateEngineService(Mock.Of<ILogger<TemplateEngineService>>(), _messages.Object);
    }

    public void Dispose() => BlocksContext.SetContext(null);

    [Fact]
    public async Task A_query_job_naming_another_tenant_is_never_queued()
    {
        var result = await _service.CreateFileWithFilteredMongoQueryAsync(new CreateFileWithFilteredMongoQueryRequest
        {
            ProjectKey = "tenant-b",
            FilteredMongoQueryDatas = [new FilteredMongoQueryData { EntityName = "Order", Text = "{}" }]
        });

        result.IsSuccess.Should().BeFalse("the Worker would open tenant B's database for this job");
        result.Message.Should().Be(CallerTenant.MismatchMessage);
        _messages.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<CreateFileWithFilteredMongoQueryEvent>>()), Times.Never);
    }

    [Fact]
    public async Task A_query_job_runs_against_the_callers_tenant()
    {
        CreateFileWithFilteredMongoQueryEvent? queued = null;
        _messages
            .Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<CreateFileWithFilteredMongoQueryEvent>>()))
            .Callback<ConsumerMessage<CreateFileWithFilteredMongoQueryEvent>>(m => queued = m.Payload)
            .Returns(Task.CompletedTask);

        await _service.CreateFileWithFilteredMongoQueryAsync(new CreateFileWithFilteredMongoQueryRequest());

        queued!.ProjectKey.Should().Be("tenant-a",
            "the Worker prefers the event's ProjectKey over its own context, so the API must stamp it");
    }

    [Fact]
    public async Task Every_template_job_refuses_another_tenant()
    {
        (await _service.RenderWithJsonAsync(new RenderWithJsonRequest { ProjectKey = "tenant-b", JSONString = "{}" })).IsSuccess.Should().BeFalse();
        (await _service.RenderWithJsonBulkAsync(new RenderWithJsonBulkRequest { ProjectKey = "tenant-b" })).IsSuccess.Should().BeFalse();
        (await _service.GenerateRenderedFileAsync(new GenerateRenderedFileRequest { ProjectKey = "tenant-b" })).IsSuccess.Should().BeFalse();
        (await _service.GenerateRenderedFilesBulkAsync(new GenerateRenderedFilesBulkRequest { ProjectKey = "tenant-b" })).IsSuccess.Should().BeFalse();
        (await _service.CreateFileWithFilteredMongoQueryBulkAsync(new CreateFileWithFilteredMongoQueryBulkRequest { ProjectKey = "tenant-b" })).IsSuccess.Should().BeFalse();
        (await _service.CreateMultipleFileWithFilteredMongoQueryAsync(new CreateMultipleFileWithFilteredMongoQueryRequest { ProjectKey = "tenant-b" })).IsSuccess.Should().BeFalse();

        _messages.Invocations.Should().BeEmpty("each of these jobs reads tenant B's storage or database in the Worker");
    }

    [Theory]
    [InlineData("ClientCredential")]
    [InlineData("User")]
    [InlineData("Role")]
    [InlineData("Permission")]
    [InlineData("Secret")]
    [InlineData("Subscription")]
    [InlineData("SubscriptionCreditGrant")]
    [InlineData("PaymentDetail")]
    [InlineData("PaymentProvider")]
    [InlineData("StoredPaymentMethod")]
    [InlineData("MagicLink")]
    [InlineData("clientcredential")]
    public void Platform_collections_cannot_be_queried(string entityName)
    {
        var query = () => MongoQueryHelper.EnsureEntityIsQueryable(entityName);

        query.Should().Throw<ArgumentException>("these hold secrets and other tenants' business records, not template data");
    }

    [Theory]
    [InlineData("system.user")]
    [InlineData("Order.x")]
    [InlineData("Order$")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1Order")]
    public void Entity_names_must_be_plain_identifiers(string entityName)
    {
        var query = () => MongoQueryHelper.EnsureEntityIsQueryable(entityName);

        query.Should().Throw<ArgumentException>("a dot reaches system.* and other namespaces");
    }

    [Theory]
    [InlineData("Order")]
    [InlineData("Invoice")]
    [InlineData("Customer_Profile")]
    public void Customer_entities_can_be_queried(string entityName)
    {
        var query = () => MongoQueryHelper.EnsureEntityIsQueryable(entityName);

        query.Should().NotThrow("the feature exists to render the tenant's own business data");
    }

    [Theory]
    [InlineData("{\"$where\": \"sleep(5000) || true\"}")]
    [InlineData("{\"$and\": [{\"Status\": \"Paid\"}, {\"$expr\": {\"$function\": {\"body\": \"return true\", \"args\": [], \"lang\": \"js\"}}}]}")]
    [InlineData("{\"Total\": {\"$gt\": 0}, \"Lines\": {\"$elemMatch\": {\"$where\": \"true\"}}}")]
    public async Task Queries_that_run_code_on_the_database_return_nothing_and_never_touch_it(string text)
    {
        var result = await _queries.GetEntityListFromQueryData(
            [new FilteredMongoQueryData { EntityName = "Order", Text = text, Key = "orders" }],
            "tenant-a");

        result.Should().ContainKey("orders").WhoseValue.Should().BeEmpty();
        _databases.Verify(d => d.GetDatabase(It.IsAny<string>()), Times.Never,
            "server-side JavaScript can stall the database or read beyond the filter");
    }

    [Fact]
    public async Task A_platform_collection_is_refused_before_the_database_is_opened()
    {
        var result = await _queries.GetEntityListFromQueryData(
            [new FilteredMongoQueryData { EntityName = "ClientCredential", Text = "{}", Key = "secrets" }],
            "tenant-a");

        result["secrets"].Should().BeEmpty();
        _databases.Verify(d => d.GetDatabase(It.IsAny<string>()), Times.Never);
    }
}
