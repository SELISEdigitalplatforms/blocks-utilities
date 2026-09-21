using FluentAssertions;
using DomainService.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Storage.DomainService.Entities;
using Storage.DomainService.Enums;
using Storage.DomainService.Services;
using StorageDriver;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.Storage;

namespace XUnitTest.PdfGenerator
{
    /// <summary>
    /// Storage driver 4.1.2 refuses an upload whose parent directory does not exist, and the ids this
    /// service passed were never real directories. These pin the find-or-create that replaced them.
    /// </summary>
    public class StorageDirectoryResolverTests
    {
        private const string Logical = "Blocks-Subscription-Financial-Documents";
        private const string Configured = "Blocks-Subscription-Financial-Documents_dev";

        private readonly Mock<IFileDirectoryRepository> _repository = new();
        private readonly Mock<IFileDirectoryManagementService> _directories = new();

        private StorageDirectoryResolver Resolver(bool configured = true) => new(
            _repository.Object,
            _directories.Object,
            new ConfigurationBuilder()
                .AddInMemoryCollection(configured
                    ? new Dictionary<string, string?> { [$"StorageDirectories:{Logical}"] = Configured }
                    : [])
                .Build(),
            NullLogger<StorageDirectoryResolver>.Instance);

        private void Existing(string name, string? id) =>
            _repository
                .Setup(x => x.GetDefaultDirectoryByModuleNameAsync(name, It.IsAny<CancellationToken>()))
                .ReturnsAsync(id is null ? null : new FileDirectory { ItemId = id });

        private void CreateReturns(DirectoryOperationResult result) =>
            _directories
                .Setup(x => x.CreateDirectoryAsync(
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<string[]?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);

        [Fact]
        public async Task An_existing_directory_is_found_by_its_configured_name_and_not_recreated()
        {
            Existing(Configured, "dir-1");

            (await Resolver().ResolveAsync(Logical)).Should().Be("dir-1");

            _directories.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task A_missing_directory_is_created_at_the_root_under_its_configured_name_once()
        {
            Existing(Configured, null);
            CreateReturns(DirectoryOperationResult.Success("dir-new"));
            var resolver = Resolver();

            (await resolver.ResolveAsync(Logical)).Should().Be("dir-new");
            (await resolver.ResolveAsync(Logical)).Should().Be("dir-new", "the id is cached for the tenant");

            _directories.Verify(x => x.CreateDirectoryAsync(
                Configured, null, Configured, null, Configured, null, null, It.IsAny<CancellationToken>()), Times.Once);
            _repository.Verify(x => x.GetDefaultDirectoryByModuleNameAsync(
                Configured, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Losing_the_creation_race_uses_the_directory_the_winner_created()
        {
            _repository
                .SetupSequence(x => x.GetDefaultDirectoryByModuleNameAsync(Configured, It.IsAny<CancellationToken>()))
                .ReturnsAsync((FileDirectory?)null)
                .ReturnsAsync(new FileDirectory { ItemId = "dir-winner" });
            CreateReturns(DirectoryOperationResult.Failure(DirectoryOperationStatus.NameConflict));

            (await Resolver().ResolveAsync(Logical)).Should().Be("dir-winner");
        }

        [Fact]
        public async Task A_directory_that_cannot_be_created_resolves_to_null_rather_than_a_fake_id()
        {
            Existing(Configured, null);
            CreateReturns(DirectoryOperationResult.Failure(DirectoryOperationStatus.NotPermitted));

            (await Resolver().ResolveAsync(Logical)).Should().BeNull();
        }

        [Fact]
        public async Task An_unconfigured_name_is_used_as_it_is()
        {
            Existing(Logical, "dir-plain");

            (await Resolver(configured: false).ResolveAsync(Logical)).Should().Be("dir-plain");
        }

        [Fact]
        public async Task An_upload_asks_the_driver_for_the_real_directory_id_not_the_logical_name()
        {
            Existing(Configured, "dir-1");
            var driver = new Mock<IStorageDriverService>();
            driver
                .Setup(x => x.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()))
                .ReturnsAsync((GetPreSignedUrlForUploadResponse?)null);

            var helper = new PdfStorageHelper(
                Mock.Of<ILogger<PdfStorageHelper>>(), driver.Object, Mock.Of<IHttpClientFactory>(), Resolver());

            await helper.SavePdfToStorage(new MemoryStream([1]), "file1", "a.pdf", parentDirectoryId: Logical);

            driver.Verify(x => x.GetPerSignedUrlForUploadAsync(
                It.Is<GetPreSignedUrlForUploadRequest>(r => r.ParentDirectoryId == "dir-1")), Times.Once);
        }

        /// <summary>
        /// The driver has no logger of its own and reports failure only through the response it
        /// returns, so a helper that drops it leaves nobody able to say why an upload failed --
        /// which is exactly what "access=forbidden" looked like on dev after the 4.1.2 upgrade.
        /// </summary>
        [Fact]
        public async Task A_refused_upload_url_is_logged_with_the_drivers_own_reason()
        {
            Existing(Configured, "dir-1");
            var logger = new Mock<ILogger<PdfStorageHelper>>();
            var driver = new Mock<IStorageDriverService>();
            driver
                .Setup(x => x.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()))
                .ReturnsAsync(new GetPreSignedUrlForUploadResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { ["access"] = "forbidden" }
                });

            var helper = new PdfStorageHelper(
                logger.Object, driver.Object, Mock.Of<IHttpClientFactory>(), Resolver());

            await helper.SavePdfToStorage(new MemoryStream([1]), "file1", "a.pdf", parentDirectoryId: Logical);

            logger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("access=forbidden")),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task An_upload_with_no_resolvable_directory_fails_without_asking_the_driver()
        {
            Existing(Configured, null);
            CreateReturns(DirectoryOperationResult.Failure(DirectoryOperationStatus.NotPermitted));
            var driver = new Mock<IStorageDriverService>(MockBehavior.Strict);

            var helper = new PdfStorageHelper(
                Mock.Of<ILogger<PdfStorageHelper>>(), driver.Object, Mock.Of<IHttpClientFactory>(), Resolver());

            (await helper.SavePdfToStorage(new MemoryStream([1]), "file1", "a.pdf", parentDirectoryId: Logical))
                .Should().BeFalse();
        }
    }
}
