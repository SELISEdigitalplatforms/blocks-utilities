using System.Security.Cryptography;
using System.Text;

namespace Payment.DomainService.Services;

public interface IShopperReferenceService
{
    bool TryCreate(string tenantId, string actorId, string key, out string shopperReference);
}
