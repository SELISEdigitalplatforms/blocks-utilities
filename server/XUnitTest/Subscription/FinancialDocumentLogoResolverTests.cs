using DomainService.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StorageDriver;
using Subscription.DomainService.Services;
using Utility.DomainService.PdfGenerator.service;

namespace XUnitTest.Subscription;

/// <summary>
/// A branding asset must never be able to take a financial document down with it.
/// </summary>
/// <remarks>
/// Byte-level validation (which signatures are accepted, what an oversized or malformed file does)
/// is covered independently in <c>FinancialDocumentLogoBytesEmbedderTests</c>, with no storage
/// involved at all -- that split is what these tests do not have to re-prove. What belongs here is
/// the fetch itself: found, not found, and unreachable, exercised through a real
/// <see cref="PdfStorageHelper"/> over a mocked <see cref="IStorageDriverService"/>, the same seam
/// <c>PdfStorageHelperTests</c> uses.
/// </remarks>
public sealed class FinancialDocumentLogoResolverTests
{
    [Fact]
    public async Task No_logo_file_id_resolves_to_nothing_and_warns_about_nothing()
    {
        var storage = new Mock<IStorageDriverService>();
        var resolver = Resolver(storage);

        var result = await resolver.ResolveAsync(null, CancellationToken.None);

        result.DataUri.Should().BeNull();
        result.WarningCode.Should().BeNull("a merchant with no logo is the ordinary case, not a failure");
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_logo_that_no_longer_resolves_in_storage_falls_back_with_a_warning()
    {
        var storage = new Mock<IStorageDriverService>();

        // The exact shape PdfStorageHelperTests already pins for "the download URL could not be
        // resolved" -- a deleted or never-existing file looks the same from here.
        storage
            .Setup(driver => driver.GetUrlForDownloadFileAsync(It.IsAny<GetFileRequest>()))
            .ReturnsAsync((FileResponse?)null);

        var resolver = Resolver(storage);

        var result = await resolver.ResolveAsync("logo-1", CancellationToken.None);

        result.DataUri.Should().BeNull();
        result.WarningCode.Should().Be("document_logo_unavailable");
    }

    [Fact]
    public async Task Storage_throwing_falls_back_with_a_warning_rather_than_failing_the_document()
    {
        var storage = new Mock<IStorageDriverService>();
        storage
            .Setup(driver => driver.GetUrlForDownloadFileAsync(It.IsAny<GetFileRequest>()))
            .ThrowsAsync(new InvalidOperationException("storage is unreachable"));

        var resolver = Resolver(storage);

        var result = await resolver.ResolveAsync("logo-1", CancellationToken.None);

        // Not rethrown. Whatever is wrong with storage is not this document's problem to fail on --
        // it renders from the merchant's name, and the warning is where the reason actually goes.
        result.DataUri.Should().BeNull();
        result.WarningCode.Should().Be("document_logo_unavailable");
    }

    private static FinancialDocumentLogoResolver Resolver(Mock<IStorageDriverService> storage) =>
        new(
            new PdfStorageHelper(
                NullLogger<PdfStorageHelper>.Instance,
                storage.Object,
                // Strict: every case here fails before a URL is ever produced, so asking the
                // factory for a client would mean the resolver had started a real download.
                new Mock<IHttpClientFactory>(MockBehavior.Strict).Object),
            NullLogger<FinancialDocumentLogoResolver>.Instance);
}
