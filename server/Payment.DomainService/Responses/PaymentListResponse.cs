namespace Payment.DomainService.Responses;

public sealed class PaymentListResponse
{
    public IReadOnlyList<PaymentListItemResponse> Items { get; init; } =
        [];

    public CursorPageInfoResponse PageInfo { get; init; } = new();
}
