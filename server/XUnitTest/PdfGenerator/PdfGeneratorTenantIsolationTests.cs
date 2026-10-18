using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Utility.DomainService.PdfGenerator;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.Shared.Utilities;

namespace XUnitTest.PdfGenerator;

/// <summary>
/// Which tenant a queued PDF job runs against.
/// </summary>
/// <remarks>
/// Guards against a signed-in user of one tenant naming another tenant's <c>ProjectKey</c> and
/// having the Worker read source files from, and write generated PDFs into, that tenant's storage.
/// </remarks>
public sealed class PdfGeneratorTenantIsolationTests : IDisposable
{
    private readonly Mock<IMessageClient> _messageClient = new();
    private readonly PdfGeneratorService _service;

    public PdfGeneratorTenantIsolationTests()
    {
        BlocksContext.SetContext(BlocksContext.Create(
            "caller-tenant", null, "user-1", true, null, "org-1",
            DateTime.UtcNow.AddHours(1), null, null, null, null, null, null, null));

        _service = new PdfGeneratorService(Mock.Of<ILogger<PdfGeneratorService>>(), _messageClient.Object);
    }

    public void Dispose() => BlocksContext.SetContext(null);

    public static TheoryData<string> Operations => new()
    {
        "Merge", "CreateFromHtml", "ExtractText", "CreateFromTemplate", "CreateFromTemplateBulk",
        "Fix", "StampImage", "StampText", "StampInto"
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Another_tenants_project_key_is_refused_and_nothing_is_queued(string operation)
    {
        var (isSuccess, message) = await Invoke(operation, "other-tenant");

        isSuccess.Should().BeFalse("the job would run against another tenant's storage");
        message.Should().Be(CallerTenant.MismatchMessage);
        _messageClient.Invocations.Should().BeEmpty("a refused request must never reach the Worker");
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task A_missing_project_key_queues_the_job_for_the_caller(string operation)
    {
        var (isSuccess, _) = await Invoke(operation, null);

        isSuccess.Should().BeTrue();
        dynamic sent = _messageClient.Invocations.Single().Arguments[0];
        ((string?)sent.Payload.ProjectKey).Should().Be("caller-tenant",
            "the Worker falls back to the event's ProjectKey, so it must be the caller's tenant");
    }

    private async Task<(bool IsSuccess, string? Message)> Invoke(string operation, string? projectKey) => operation switch
    {
        "Merge" => Pick(await _service.MergePdfsAsync(new MergePdfsRequest { ProjectKey = projectKey })),
        "CreateFromHtml" => Pick(await _service.CreatePdfsFromHtmlAsync(new CreatePdfsFromHtmlRequest { ProjectKey = projectKey })),
        "ExtractText" => Pick(await _service.ExtractTextFromPdfsAsync(new ExtractTextFromPdfsRequest { ProjectKey = projectKey })),
        "CreateFromTemplate" => Pick(await _service.CreatePdfsFromHtmlUsingTEAsync(new CreatePdfsFromHtmlUsingTERequest { ProjectKey = projectKey })),
        "CreateFromTemplateBulk" => Pick(await _service.CreatePdfsFromHtmlUsingTEBulkAsync(new CreatePdfsFromHtmlUsingTEBulkRequest { ProjectKey = projectKey })),
        "Fix" => Pick(await _service.FixPdfsAsync(new FixPdfsRequest { ProjectKey = projectKey, PdfInfos = [new FixPdfCommand()] })),
        "StampImage" => Pick(await _service.StampImageToPdfAsync(new StampImageToPdfRequest { ProjectKey = projectKey })),
        "StampText" => Pick(await _service.StampTextToPdfAsync(new StampTextToPdfRequest { ProjectKey = projectKey })),
        "StampInto" => Pick(await _service.StampIntoPdfAsync(new StampIntoPdfRequest { ProjectKey = projectKey })),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static (bool, string?) Pick(dynamic response) => (response.IsSuccess, response.Message);
}
