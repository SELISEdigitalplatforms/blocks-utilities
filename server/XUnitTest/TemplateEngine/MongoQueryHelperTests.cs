using System.Reflection;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Utility.DomainService.TemplateEngine;
using Utility.DomainService.TemplateEngine.service;

namespace XUnitTest.TemplateEngine
{
    public class MongoQueryHelperTests
    {
        private readonly Mock<ILogger<MongoQueryHelper>> _loggerMock;
        private readonly Mock<IDbContextProvider> _dbContextMock;
        private readonly Mock<IMongoDatabase> _databaseMock;

        private readonly MongoQueryHelper _helper;

        public MongoQueryHelperTests()
        {
            _loggerMock = new Mock<ILogger<MongoQueryHelper>>();
            _dbContextMock = new Mock<IDbContextProvider>();
            _databaseMock = new Mock<IMongoDatabase>();

            _dbContextMock
                .Setup(x => x.GetDatabase(It.IsAny<string>()))
                .Returns(_databaseMock.Object);

            _helper = new MongoQueryHelper(_loggerMock.Object, _dbContextMock.Object);
        }

        [Fact]
        public void GetMetaDataListFromData_ReturnsDictionary()
        {
            var metaList = new List<MetaData>
            {
                new MetaData { Name = "Key1", Value = "Value1" },
                new MetaData { Name = "Key2", Value = "Value2" }
            };

            var result = MongoQueryHelper.GetMetaDataListFromData(metaList);

            Assert.Equal(2, result.Count);
            Assert.Equal("Value1", result["Key1"]);
            Assert.NotNull(result["Key2"]);
        }

        [Fact]
        public async Task GetEntityListFromQueryData_ReturnsData_WithPagination()
        {
            var queryData = new FilteredMongoQueryData
            {
                EntityName = "Order",
                Text = "{}",
                FetchAllMatchedItem = false,
                OrderBy = "Name",
                SortOrder = SortOrder.Ascending,
                PageNumber = 0,
                PageLimit = 10
            };

            var bson = new BsonDocument
            {
                { "ItemId", "1" },
                { "Name", "TestOrder" }
            };

            var cursorMock = new Mock<IAsyncCursor<BsonDocument>>();
            cursorMock.SetupSequence(x => x.MoveNext(It.IsAny<CancellationToken>()))
                      .Returns(true)
                      .Returns(false);
            cursorMock.Setup(x => x.Current)
                      .Returns(new[] { bson });

            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();
            
            collectionMock.Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _databaseMock
                .Setup(x => x.GetCollection<BsonDocument>("Orders", null))
                .Returns(collectionMock.Object);

            var result = await _helper.GetEntityListFromQueryData(
                new List<FilteredMongoQueryData> { queryData },
                "tenant1");

            Assert.Single(result);
        }

        [Fact]
        public async Task GetEntityListFromQueryData_FetchAllPath_ReturnsData()
        {
            var queryData = new FilteredMongoQueryData
            {
                EntityName = "Order",
                Text = "{}",
                FetchAllMatchedItem = true,
                OrderBy = "Name",
                SortOrder = SortOrder.Ascending
            };

            var bson = new BsonDocument { { "ItemId", "2" } };

            var cursorMock = new Mock<IAsyncCursor<BsonDocument>>();
            cursorMock.SetupSequence(x => x.MoveNext(It.IsAny<CancellationToken>()))
                      .Returns(true)
                      .Returns(false);
            cursorMock.Setup(x => x.Current)
                      .Returns(new[] { bson });

            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();
            collectionMock.Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            _databaseMock
                .Setup(x => x.GetCollection<BsonDocument>("Orders", null))
                .Returns(collectionMock.Object);

            var result = await _helper.GetEntityListFromQueryData(
                new List<FilteredMongoQueryData> { queryData },
                "tenant1");

            Assert.NotEmpty(result);
        }

        [Fact]
        public void BuildMongoFilter_InvalidJson_ThrowsArgumentException()
        {
            var method = typeof(MongoQueryHelper)
                .GetMethod("BuildMongoFilter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var ex = Assert.Throws<TargetInvocationException>(() =>
                method!.Invoke(_helper, new object[] { "{ invalid json }" }));

            Assert.IsType<ArgumentException>(ex.InnerException);
        }
    }
}
