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
/// Exercised through a real <see cref="PdfStorageHelper"/> over a mocked
/// <see cref="IStorageDriverService"/>, the same seam <c>PdfStorageHelperTests</c> uses — not
/// because the resolver needs storage internals, but because that is the only injectable point in
/// this pipeline; <see cref="Utility.DomainService.Storage.StorageHelperBase"/> opens its own
/// <see cref="HttpClient"/> rather than accepting one, so a case that would only diverge once a
/// download URL exists (bad magic bytes, an oversized file, malformed SVG) is not something a unit
/// test in this suite can reach — that is a pre-existing gap in the storage module, not one this
/// feature introduces, and worth a follow-up to make the byte-validation logic independently
/// testable.
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

    private static FinancialDocumentLogoResolver Resolver(Mock<IStorageDriverService> storage) =>
        new(
            new PdfStorageHelper(NullLogger<PdfStorageHelper>.Instance, storage.Object),
            NullLogger<FinancialDocumentLogoResolver>.Instance);
}
