namespace Payment.DomainService.Enums;

public static class PaymentQuerySortDirections
{
    public const string Ascending = "asc";
    public const string Descending = "desc";

    public static readonly string[] All =
    [
        Ascending,
        Descending
    ];
}
