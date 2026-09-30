using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Utility.DomainService.PdfIngestion;
using Utility.DomainService.PdfSignatureValidation.Entities;
using Utility.DomainService.PdfSignatureValidation.service;

namespace XUnitTest.Integration;

/// <remarks>
/// Guards spec AC-4 against a real MongoDB: when a file is re-requested while an earlier run is
/// still working, the earlier run finishing last must not overwrite the newer request with a stale
/// verdict. The guard is a filter on the stored run ID, which only a real database can prove.
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class PdfSignatureValidationRepositoryIntegrationTests
{
    private readonly PdfSignatureValidationRepository _repository;

    public PdfSignatureValidationRepositoryIntegrationTests(MongoIntegrationFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        _repository = new PdfSignatureValidationRepository(
            NullLogger<PdfSignatureValidationRepository>.Instance,
            fixture.DbContextProvider);
    }

    private static PdfSignatureValidationJob Queued(string fileId, string tenantId) => new()
    {
        Id = fileId,
        RunId = Guid.NewGuid().ToString("N"),
        TenantId = tenantId,
        Status = PdfIngestionStatus.Queued,
        CreateDate = DateTime.UtcNow,
        LastUpdateDate = DateTime.UtcNow
    };

    [Fact]
    public async Task A_superseded_run_finishing_last_does_not_overwrite_the_newer_request()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var fileId = Guid.NewGuid().ToString("N");
        var firstRun = Queued(fileId, tenantId);
        (await _repository.SaveJobAsync(firstRun, tenantId)).Should().BeTrue();

        // The file is re-requested while the first run is still working.
        var secondRun = Queued(fileId, tenantId);
        (await _repository.SaveJobAsync(secondRun, tenantId)).Should().BeTrue();

        // Then the first run finishes and tries to write its verdict.
        firstRun.Status = PdfIngestionStatus.Completed;
        firstRun.Verdict = new PdfSignatureValidationVerdict { SignatureCount = 1, AllPassed = true };
        var written = await _repository.UpdateJobIfCurrentRunAsync(firstRun, tenantId);

        written.Should().BeFalse("the first run was superseded, so its write belongs to nobody");
        var stored = (await _repository.GetJobsAsync([fileId], tenantId)).Should().ContainSingle().Subject;
        stored.RunId.Should().Be(secondRun.RunId);
        stored.Status.Should().Be(
            PdfIngestionStatus.Queued, "the poller must see the newer request, not the stale verdict");
        stored.Verdict.Should().BeNull();
    }

    [Fact]
    public async Task The_current_run_writes_its_verdict()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var job = Queued(Guid.NewGuid().ToString("N"), tenantId);
        await _repository.SaveJobAsync(job, tenantId);

        job.Status = PdfIngestionStatus.Completed;
        job.Verdict = new PdfSignatureValidationVerdict
        {
            SignatureCount = 1,
            LowestLevel = "PAdES-BASELINE-LT",
            AllPassed = true,
            Signatures = [new PdfSignatureVerdict { Indication = "TOTAL_PASSED", RevocationOrigin = "DssDictionary" }]
        };
        var written = await _repository.UpdateJobIfCurrentRunAsync(job, tenantId);

        written.Should().BeTrue("a run that was not superseded owns its record");
        var stored = (await _repository.GetJobsAsync([job.Id], tenantId)).Should().ContainSingle().Subject;
        stored.Status.Should().Be(PdfIngestionStatus.Completed);
        stored.Verdict!.LowestLevel.Should().Be("PAdES-BASELINE-LT", "the verdict must survive the round trip intact");
        stored.Verdict.Signatures.Should().ContainSingle().Which.RevocationOrigin.Should().Be(
            "DssDictionary", "per-signature results are the point of the verdict and must be stored too");
    }
}
