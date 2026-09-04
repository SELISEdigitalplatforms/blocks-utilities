using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Moq;
using Utility.DomainService.PdfGenerator.Entities;
using Utility.DomainService.PdfGenerator.service;

namespace XUnitTest.PdfGenerator
{
    public class PdfGeneratorRepositoryTests
    {
        private readonly Mock<ILogger<PdfGeneratorRepository>> _loggerMock;
        private readonly Mock<IDbContextProvider> _dbContextProviderMock;
        private readonly Mock<IMongoDatabase> _databaseMock;

        private readonly PdfGeneratorRepository _repository;

        public PdfGeneratorRepositoryTests()
        {
            _loggerMock = new Mock<ILogger<PdfGeneratorRepository>>();
            _dbContextProviderMock = new Mock<IDbContextProvider>();
            _databaseMock = new Mock<IMongoDatabase>();

            _dbContextProviderMock
                .Setup(x => x.GetDatabase(It.IsAny<string>()))
                .Returns(_databaseMock.Object);

            _repository = new PdfGeneratorRepository(
                _loggerMock.Object,
                _dbContextProviderMock.Object);
        }

        [Fact]
        public async Task GetPdfUtilityProfileAsync_ReturnsNull_WhenNotFound()
        {
            // Arrange
            var cursorMock = new Mock<IAsyncCursor<PdfUtilityProfile>>();
            cursorMock.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>()))
                      .Returns(false);

            var collectionMock = new Mock<IMongoCollection<PdfUtilityProfile>>();
            collectionMock
                .Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<PdfUtilityProfile>>(),
                    It.IsAny<FindOptions<PdfUtilityProfile, PdfUtilityProfile>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _databaseMock
                .Setup(db => db.GetCollection<PdfUtilityProfile>("PdfUtilityProfiles", null))
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.GetPdfUtilityProfileAsync("missing", "tenant1");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task SavePdfExtractDumpAsync_ReturnsTrue_WhenInsertSucceeds()
        {
            // Arrange
            var dump = new PdfExtractDump { ItemId = "item-1" };

            var collectionMock = new Mock<IMongoCollection<PdfExtractDump>>();
            collectionMock
                .Setup(c => c.InsertOneAsync(
                    dump,
                    null,
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _databaseMock
                .Setup(db => db.GetCollection<PdfExtractDump>("PdfExtractDumps", null))
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.SavePdfExtractDumpAsync(dump, "tenant1");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task SavePdfExtractDumpAsync_ReturnsFalse_OnException()
        {
            // Arrange
            var dump = new PdfExtractDump { ItemId = "item-1" };

            var collectionMock = new Mock<IMongoCollection<PdfExtractDump>>();
            collectionMock
                .Setup(c => c.InsertOneAsync(
                    It.IsAny<PdfExtractDump>(),
                    null,
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Mongo error"));

            _databaseMock
                .Setup(db => db.GetCollection<PdfExtractDump>("PdfExtractDumps", null))
                .Returns(collectionMock.Object);

            // Act
            var result = await _repository.SavePdfExtractDumpAsync(dump, "tenant1");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task PdfExtractDumpExistsAsync_ReturnsTrue_WhenCountGreaterThanZero()
        {
            // Arrange
            var collectionMock = new Mock<IMongoCollection<PdfExtractDump>>();
            collectionMock
                .Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<PdfExtractDump>>(),
                    null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);

            _databaseMock
                .Setup(db => db.GetCollection<PdfExtractDump>("PdfExtractDumps", null))
                .Returns(collectionMock.Object);

            // Act
            var exists = await _repository.PdfExtractDumpExistsAsync("item-1", "tenant1");

            // Assert
            Assert.True(exists);
        }

        [Fact]
        public async Task PdfExtractDumpExistsAsync_ReturnsFalse_OnException()
        {
            // Arrange
            var collectionMock = new Mock<IMongoCollection<PdfExtractDump>>();
            collectionMock
                .Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<PdfExtractDump>>(),
                    null,
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Mongo error"));

            _databaseMock
                .Setup(db => db.GetCollection<PdfExtractDump>("PdfExtractDumps", null))
                .Returns(collectionMock.Object);

            // Act
            var exists = await _repository.PdfExtractDumpExistsAsync("item-1", "tenant1");

            // Assert
            Assert.False(exists);
        }


        [Fact]
        public async Task GetDocumentConversionJobsAsync_EmptyIdList_ReturnsEmptyWithoutQueryingMongo()
        {
            // A batch caller that somehow ends up asking about zero files should get an empty
            // result immediately rather than round-tripping to Mongo for an $in filter with no
            // values in it.
            var jobs = await _repository.GetDocumentConversionJobsAsync(Array.Empty<string>(), "tenant1");

            Assert.Empty(jobs);
            _databaseMock.Verify(db => db.GetCollection<DocumentConversionJob>(It.IsAny<string>(), null), Times.Never);
        }

    }
}
