using Payment.DomainService.Models;

namespace Payment.DomainService.Repositories;

public interface IPaymentQueryRepository
{
    Task<PaymentQueryPage> QueryAsync(
        PaymentQueryCriteria criteria,
        CancellationToken cancellationToken);
}
