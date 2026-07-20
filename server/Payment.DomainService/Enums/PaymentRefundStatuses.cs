namespace Payment.DomainService.Enums;

public static class PaymentRefundStatuses
{
    public const string Initiating = "INITIATING";

    public const string Submitted = "SUBMITTED";

    public const string InitiationUnknown =
        "INITIATION_UNKNOWN";

    public const string Succeeded = "SUCCEEDED";

    public const string Failed = "FAILED";

    public const string Reversed = "REVERSED";

    public const string RequiresAttention =
        "REQUIRES_ATTENTION";
}
