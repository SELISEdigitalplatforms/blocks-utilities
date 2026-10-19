using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Utility.DomainService.MagicLink;
using Utility.DomainService.MagicLink.Models;
using Utility.DomainService.MagicLink.Service;
using Utility.DomainService.Shared.Utilities;
using MagicLinkEntity = Utility.DomainService.MagicLink.Models.MagicLink;

namespace XUnitTest.MagicLink;

/// <summary>
/// Every magic link lives in the root database and the Worker opens <c>link.ProjectKey</c>'s
/// database to fetch the link's client credential.
/// </summary>
/// <remarks>
/// Guards against a signed-in user of one tenant creating, reading, disabling or reconfiguring
/// another tenant's links by sending that tenant's key as <c>ProjectKey</c>. The worst case was a
/// link carrying the other tenant's client credential: invoking it had the Worker mint that
/// tenant's service token and send it to a URL the attacker chose.
/// </remarks>
public sealed class MagicLinkTenantIsolationTests : IDisposable
{
    private const string CallerTenantId = "tenant-a";
    private const string OtherTenantId = "tenant-b";

    private readonly Mock<IMagicLinkRepository> _repository = new();
    private readonly Mock<ICacheClient> _cache = new();
    private readonly Mock<IMessageClient> _messages = new();
    private readonly MagicLinkService _service;

    public MagicLinkTenantIsolationTests()
    {
        SignInAs(CallerTenantId);

        var configuration = new Mock<IConfiguration>();
        configuration.Setup(c => c["RootTenantId"]).Returns("root-tenant");

        _repository
            .Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((MagicLinkEntity?)null);

        _service = new MagicLinkService(
            Mock.Of<ILogger<MagicLinkService>>(),
            _repository.Object,
            _cache.Object,
            _messages.Object,
            configuration.Object);
    }

    public void Dispose() => BlocksContext.SetContext(null);

    [Fact]
    public async Task A_link_cannot_be_created_in_another_tenant()
    {
        var result = await _service.CreateLinkAsync(new CreateMagicLinkRequest
        {
            Type = MagicLinkType.Action,
            Uri = "https://attacker.example/collect",
            RequestMethod = "GET",
            ClientCredential = "tenant-b-service-client",
            ProjectKey = OtherTenantId
        });

        result.IsSuccess.Should().BeFalse("a link stored under tenant B would make the Worker mint tenant B's token");
        result.ErrorMessage.Should().Be(CallerTenant.MismatchMessage);
        _repository.Verify(r => r.CreateMagicLinkAsync(It.IsAny<MagicLinkEntity>()), Times.Never);
    }

    [Fact]
    public async Task A_link_without_a_ProjectKey_belongs_to_the_callers_tenant()
    {
        MagicLinkEntity? stored = null;
        _repository
            .Setup(r => r.CreateMagicLinkAsync(It.IsAny<MagicLinkEntity>()))
            .Callback<MagicLinkEntity>(link => stored = link)
            .ReturnsAsync("link-id");

        await _service.CreateLinkAsync(new CreateMagicLinkRequest
        {
            Type = MagicLinkType.Action,
            Uri = "https://api.test.com/endpoint",
            RequestMethod = "GET",
            ClientCredential = "tenant-a-service-client"
        });

        stored!.ProjectKey.Should().Be(CallerTenantId,
            "the client credential is looked up in this tenant's database when the link is invoked, " +
            "not the root tenant's");
    }

    [Fact]
    public async Task A_ProjectKey_naming_the_callers_own_tenant_is_still_accepted()
    {
        _repository.Setup(r => r.CreateMagicLinkAsync(It.IsAny<MagicLinkEntity>())).ReturnsAsync("link-id");

        var result = await _service.CreateLinkAsync(new CreateMagicLinkRequest
        {
            Type = MagicLinkType.Redirect,
            Uri = "https://example.com",
            ProjectKey = CallerTenantId.ToUpperInvariant()
        });

        result.IsSuccess.Should().BeTrue("existing integrations send their own key and must keep working");
    }

    [Fact]
    public async Task Nothing_is_created_without_a_signed_in_tenant()
    {
        BlocksContext.SetContext(null);

        var result = await _service.CreateLinkAsync(new CreateMagicLinkRequest
        {
            Type = MagicLinkType.Redirect,
            Uri = "https://example.com"
        });

        result.IsSuccess.Should().BeFalse("with no tenant there is nothing to scope the link to, so fail closed");
        _repository.Verify(r => r.CreateMagicLinkAsync(It.IsAny<MagicLinkEntity>()), Times.Never);
    }

    [Fact]
    public async Task Another_tenants_links_cannot_be_listed()
    {
        var result = await _service.GetLinksAsync(new GetMagicLinksRequest { ProjectKey = OtherTenantId, PageSize = 10 });

        result.IsSuccess.Should().BeFalse("listing would expose tenant B's link targets and payloads");
        _repository.Verify(r => r.GetMagicLinksAsync(It.IsAny<GetMagicLinksRequest>()), Times.Never);
    }

    [Fact]
    public async Task Listing_is_filtered_to_the_callers_tenant_even_when_no_ProjectKey_is_sent()
    {
        GetMagicLinksRequest? query = null;
        _repository
            .Setup(r => r.GetMagicLinksAsync(It.IsAny<GetMagicLinksRequest>()))
            .Callback<GetMagicLinksRequest>(r => query = r)
            .ReturnsAsync((new List<MagicLinkEntity>(), 0));

        await _service.GetLinksAsync(new GetMagicLinksRequest { PageSize = 10 });

        query!.ProjectKey.Should().Be(CallerTenantId, "an empty key used to match the root tenant's links");
    }

    [Fact]
    public async Task Reading_a_link_by_id_is_scoped_to_the_callers_tenant()
    {
        await _service.GetLinkAsync(new GetMagicLinkRequest { ItemId = "AbCdEf" });

        _repository.Verify(r => r.GetMagicLinkAsync("AbCdEf", CallerTenantId), Times.Once,
            "the repository used to ignore the key, so any tenant could read any link by its id");
    }

    [Fact]
    public async Task Another_tenants_links_cannot_be_disabled()
    {
        var result = await _service.RemoveLinksAsync(new RemoveMagicLinksRequest
        {
            LinkIds = ["AbCdEf"],
            ProjectKey = OtherTenantId
        });

        result.IsSuccess.Should().BeFalse("disabling tenant B's links would break their customer-facing links");
        _repository.Verify(r => r.GetMagicLinksByIdsAsync(It.IsAny<List<string>>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Another_tenants_link_configuration_cannot_be_overwritten()
    {
        var result = await _service.SaveLinkBasedActionConfigAsync(new SaveLinkBasedActionConfigRequest
        {
            ShortUrlBase = "https://attacker.example",
            ProjectKey = OtherTenantId
        });

        result.IsSuccess.Should().BeFalse("ShortUrlBase decides where tenant B's new short links point");
        _repository.Verify(r => r.GetLinkBasedActionConfigAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Invoking_a_link_finds_it_by_id_alone()
    {
        BlocksContext.SetContext(null);
        _repository
            .Setup(r => r.GetMagicLinkAsync("AbCdEf", null))
            .ReturnsAsync(new MagicLinkEntity
            {
                ItemId = "AbCdEf",
                Type = MagicLinkType.Redirect,
                Uri = "https://example.com",
                ProjectKey = OtherTenantId
            });

        var result = await _service.InvokeLinkAsync(new InvokeMagicLinkRequest { LinkId = "AbCdEf", ProjectKey = "root-tenant" });

        result.IsSuccess.Should().BeTrue(
            "Invoke is anonymous and the stored link already carries its tenant, so the root-tenant " +
            "key the controller passes must not hide a project's link");
    }

    private static void SignInAs(string tenantId) =>
        BlocksContext.SetContext(BlocksContext.Create(
            tenantId, null, "user-1", true, null, "org-1",
            DateTime.UtcNow.AddHours(1), null, null, null, null, null, null, null));
}
