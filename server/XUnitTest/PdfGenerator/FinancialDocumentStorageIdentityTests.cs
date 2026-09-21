using Blocks.Genesis;
using DomainService.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StorageDriver;
using Subscription.DomainService.Services;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.Storage;

namespace XUnitTest.PdfGenerator
{
    /// <summary>
    /// Document PDFs are created Creator-only under one service principal, so the storage API refuses
    /// them to every other tenant user while this service can still write and read them back.
    /// </summary>
    public class FinancialDocumentStorageIdentityTests : IDisposable
    {
        private readonly Mock<IStorageDriverService> _driver = new();

        public FinancialDocumentStorageIdentityTests() =>
            BlocksContext.SetContext(BlocksContext.Create(
                "tenant-1", ["billingowner"], "subscriber-7", true, null, "org-1",
                DateTime.UtcNow.AddHours(1), null, null, null, null, null, null, "tenant-1"));

        public void Dispose() => BlocksContext.SetContext(null);

        private StorageDriverFinancialDocumentFileStore Store() => new(new PdfStorageHelper(
            Mock.Of<ILogger<PdfStorageHelper>>(), _driver.Object, Mock.Of<IHttpClientFactory>(), null));

        [Fact]
        public async Task A_document_is_uploaded_creator_only_as_the_service_principal()
        {
            string? callerAtUpload = null;
            GetPreSignedUrlForUploadRequest? sent = null;
            _driver
                .Setup(x => x.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()))
                .Callback<GetPreSignedUrlForUploadRequest>(request =>
                {
                    sent = request;
                    callerAtUpload = BlocksContext.GetContext()?.UserId;
                })
                .ReturnsAsync((GetPreSignedUrlForUploadResponse?)null);

            await Store().SaveAsync("doc-1", "INV-1.pdf", [1], CancellationToken.None);

            callerAtUpload.Should().Be(StorageDriverFinancialDocumentFileStore.StoragePrincipal,
                "the file's owner is whoever uploads it, and only the owner may read a Creator-only file");
            sent!.ObjectAccessLevel.Should().Be("Creator");
            BlocksContext.GetContext()!.UserId.Should().Be("subscriber-7", "the caller's identity is restored afterwards");
        }

        [Fact]
        public async Task A_document_is_read_back_as_the_same_principal_not_the_requesting_user()
        {
            string? callerAtRead = null;
            _driver
                .Setup(x => x.GetUrlForDownloadFileAsync(It.IsAny<GetFileRequest>()))
                .Callback(() => callerAtRead = BlocksContext.GetContext()?.UserId)
                .ReturnsAsync((FileResponse?)null);

            await Store().ReadAsync("doc-1", CancellationToken.None);

            callerAtRead.Should().Be(StorageDriverFinancialDocumentFileStore.StoragePrincipal);
            BlocksContext.GetContext()!.UserId.Should().Be("subscriber-7");
        }

        [Fact]
        public void The_service_identity_keeps_the_callers_tenant_and_organization()
        {
            using (StorageServiceIdentity.Enter("svc"))
            {
                var context = BlocksContext.GetContext()!;
                context.UserId.Should().Be("svc");
                context.TenantId.Should().Be("tenant-1");
                context.OrganizationId.Should().Be("org-1");
                context.Roles.Should().BeEmpty("the principal acts as itself, not with the caller's roles");
            }

            BlocksContext.GetContext()!.UserId.Should().Be("subscriber-7");
        }

        [Fact]
        public void The_service_identity_refuses_to_act_without_a_tenant()
        {
            BlocksContext.SetContext(null);

            var enter = () => StorageServiceIdentity.Enter("svc");

            enter.Should().Throw<InvalidOperationException>();
        }
    }
}
