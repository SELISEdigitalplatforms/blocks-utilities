using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Utility.DomainService.TemplateEngine.service;

namespace XUnitTest.TemplateEngine
{
    public class TemplateEngineRepositoryTests
    {
        private readonly Mock<ILogger<TemplateEngineRepository>> _loggerMock;
        private readonly Mock<IDbContextProvider> _dbContextMock;
        private readonly Mock<IMongoDatabase> _databaseMock;

        private readonly TemplateEngineRepository _repository;

        public TemplateEngineRepositoryTests()
        {
            _loggerMock = new Mock<ILogger<TemplateEngineRepository>>();
            _dbContextMock = new Mock<IDbContextProvider>();
            _databaseMock = new Mock<IMongoDatabase>();

            _dbContextMock
                .Setup(x => x.GetDatabase(It.IsAny<string>()))
                .Returns(_databaseMock.Object);

            _repository = new TemplateEngineRepository(
                _loggerMock.Object,
                _dbContextMock.Object);
        }

        [Fact]
        public async Task GetTemplateByIdAsync_ReturnsNull_OnException()
        {
            _dbContextMock
                .Setup(x => x.GetDatabase(It.IsAny<string>()))
                .Throws(new Exception());

            var result = await _repository.GetTemplateByIdAsync("1");

            Assert.Null(result);
        }

        [Fact]
        public async Task SaveTemplateAsync_ReturnsTrue_WhenReplaceSucceeds()
        {
            var template = new HtmlTemplate { ItemId = "1", Name = "Test" };

            var collectionMock = new Mock<IMongoCollection<HtmlTemplate>>();
            collectionMock
                .Setup(x => x.ReplaceOneAsync(
                    It.IsAny<FilterDefinition<HtmlTemplate>>(),
                    template,
                    It.Is<ReplaceOptions>(o => o.IsUpsert),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<ReplaceOneResult>());

            _databaseMock
                .Setup(x => x.GetCollection<HtmlTemplate>("HtmlTemplates", null))
                .Returns(collectionMock.Object);

            var result = await _repository.SaveTemplateAsync(template);

            Assert.True(result);
        }

        [Fact]
        public async Task TemplateExistsAsync_ReturnsFalse_OnException()
        {
            _dbContextMock
                .Setup(x => x.GetDatabase(It.IsAny<string>()))
                .Throws(new Exception());

            var result = await _repository.TemplateExistsAsync("1");

            Assert.False(result);
        }

        [Fact]
        public async Task TemplateExistsAsync_ReturnsTrue_WhenCountGreaterThanZero()
        {
            var collectionMock = new Mock<IMongoCollection<HtmlTemplate>>();
            collectionMock
                .Setup(x => x.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<HtmlTemplate>>(),
                    null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(2);

            _databaseMock
                .Setup(x => x.GetCollection<HtmlTemplate>("HtmlTemplates", null))
                .Returns(collectionMock.Object);

            var result = await _repository.TemplateExistsAsync("1");

            Assert.True(result);
        }

        [Fact]
        public async Task GetUserReadableFieldsAsync_ReturnsEmpty_WhenNotFound()
        {
            var cursorMock = new Mock<IAsyncCursor<UserReadableData>>();
            cursorMock.Setup(x => x.MoveNext(It.IsAny<CancellationToken>()))
                      .Returns(false);

            var collectionMock = new Mock<IMongoCollection<UserReadableData>>();
            collectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<UserReadableData>>(),
                    It.IsAny<FindOptions<UserReadableData, UserReadableData>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _databaseMock
                .Setup(x => x.GetCollection<UserReadableData>("UserReadableDatas", null))
                .Returns(collectionMock.Object);

            var result = await _repository.GetUserReadableFieldsAsync("Test");

            Assert.Empty(result);
        }
    }
}
