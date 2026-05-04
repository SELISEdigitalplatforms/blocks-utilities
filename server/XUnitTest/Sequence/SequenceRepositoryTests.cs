using Blocks.Genesis;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Utility.DomainService.Sequence.service;

namespace XUnitTest.Sequence
{
    public class SequenceRepositoryTests
    {
        private readonly Mock<IDbContextProvider> _dbContextProvider = new();
        private readonly Mock<IMongoCollection<BsonDocument>> _collection = new();

        private readonly SequenceRepository _repository;

        public SequenceRepositoryTests()
        {
            _dbContextProvider
                .Setup(d => d.GetCollection<BsonDocument>("Sequence"))
                .Returns(_collection.Object);

            _repository = new SequenceRepository(_dbContextProvider.Object);
        }

        [Fact]
        public async Task GetNextSequenceNumberAsync_ShouldReturnCurrentNumber()
        {
            _collection
                .Setup(c => c.FindOneAndUpdateAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<FindOneAndUpdateOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BsonDocument { ["CurrentNumber"] = 42L });

            var result = await _repository.GetNextSequenceNumberAsync("invoice");

            result.Should().Be(42L);
        }

        [Fact]
        public async Task GetNextHexSequenceNumberAsync_ShouldAddInitialValue()
        {
            _collection
                .Setup(c => c.FindOneAndUpdateAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<FindOneAndUpdateOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BsonDocument { ["CurrentNumber"] = 10L });

            var result = await _repository.GetNextHexSequenceNumberAsync("invoice");

            result.Should().Be(4394967306L);
        }

        [Fact]
        public async Task ResetSequenceNumberAsync_ShouldUpsertValue()
        {
            _collection
                .Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<UpdateResult>());

            await _repository.ResetSequenceNumberAsync("invoice", 100);

            _collection.Verify(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<UpdateDefinition<BsonDocument>>(),
                It.Is<UpdateOptions>(o => o.IsUpsert),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
