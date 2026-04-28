using System.Linq.Expressions;

namespace Mfa.DomainService.Services
{
    public interface IMfaManagementRepository
    {
        Task SaveAsync<T>(T data, string collectionName = "");
        Task SaveAsync<T>(List<T> listOfData);

        Task<IList<T>> GetItemsAsync<T>(Expression<Func<T, bool>> filterExpression, string collectionName = "");
        Task<T> GetItemAsync<T>(Expression<Func<T, bool>> filterExpression, string collectionName = "");

        Task DeleteItemsAsync<T>(Expression<Func<T, bool>> dataFilters);
        Task UpsertAsync<T>(T data, Expression<Func<T, bool>> filterExpression, string collectionName = "");
        Task UpdatePartialAsync<T>(string id, Dictionary<string, object> updates, string collectionName = "");
    }
}
