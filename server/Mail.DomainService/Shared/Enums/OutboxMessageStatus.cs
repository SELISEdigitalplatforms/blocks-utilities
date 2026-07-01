namespace Mail.DomainService.Shared.Enums
{
    public enum OutboxMessageStatus
    {
        Pending = 0,
        Publishing = 1,
        Published = 2,
        FailedRetryable = 3,
        DeadLettered = 4
    }
}
