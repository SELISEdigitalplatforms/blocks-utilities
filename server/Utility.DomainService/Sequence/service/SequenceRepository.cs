using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Utility.DomainService.Sequence.service
{
    public class SequenceRepository : ISequenceRepository
    {
        private readonly IMongoCollection<BsonDocument> _collection;
        private static long INITIAL_VALUE = 4394967296; // 4294967296 -> 0x100000000 to FINAL_VALUE = 68719476735 -> 0xFFFFFFFFF


        public SequenceRepository(IDbContextProvider dbContextProvider)
        {
            _collection = dbContextProvider.GetCollection<BsonDocument>("Sequence");
        }

        public async Task<long> GetNextSequenceNumberAsync(string context)
        {
            var filter = Builders<BsonDocument>.Filter.Eq("Context", context);
            var update = Builders<BsonDocument>.Update.Inc<long>("CurrentNumber", 1);
            var options = new FindOneAndUpdateOptions<BsonDocument>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            };

            var result = await _collection.FindOneAndUpdateAsync(filter, update, options);
            return result["CurrentNumber"].AsInt64;
        }

        public async Task<long> GetNextHexSequenceNumberAsync(string context)
        {
            var filter = Builders<BsonDocument>.Filter.Eq("Context", context);
            var update = Builders<BsonDocument>.Update.Inc<long>("CurrentNumber", 1);
            var options = new FindOneAndUpdateOptions<BsonDocument>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            };

            var result = await _collection.FindOneAndUpdateAsync(filter, update, options);
            var currentNumber = result["CurrentNumber"].AsInt64;
            var convertedSequence = INITIAL_VALUE + currentNumber;
            return convertedSequence;
        }
        public async Task ResetSequenceNumberAsync(string context, long startNumber)
        {
            var filter = Builders<BsonDocument>.Filter.Eq("Context", context);
            var update = Builders<BsonDocument>.Update.Set("CurrentNumber", startNumber);
            var options = new UpdateOptions { IsUpsert = true };

            await _collection.UpdateOneAsync(filter, update, options);
        }
    }
}