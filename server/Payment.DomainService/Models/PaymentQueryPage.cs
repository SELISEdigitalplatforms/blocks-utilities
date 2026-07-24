namespace Payment.DomainService.Models;

public sealed record PaymentQueryPage(
    IReadOnlyList<PaymentQueryRecord> Items,
    bool HasMoreInQueryDirection);
