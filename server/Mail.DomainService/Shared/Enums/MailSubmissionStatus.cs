namespace Mail.DomainService.Shared.Enums
{
    public enum MailSubmissionStatus
    {
        Queued = 0,
        Processing = 1,
        Accepted = 2,
        FailedRetryable = 3,
        FailedPermanent = 4
    }
}
