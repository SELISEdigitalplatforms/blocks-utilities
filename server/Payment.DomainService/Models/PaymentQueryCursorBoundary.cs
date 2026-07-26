namespace Payment.DomainService.Models;

public sealed record PaymentQueryCursorBoundary(
    string PaymentDetailId,
    string? TextValue,
    decimal? AmountValue,
    DateTime? PaymentDateUtc);
