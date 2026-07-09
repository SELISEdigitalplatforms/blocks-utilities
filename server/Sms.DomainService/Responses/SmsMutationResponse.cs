namespace Sms.DomainService.Responses;

public class SmsMutationResponse
{
    public bool IsSuccess { get; set; }
    public string? MessageId { get; set; }
    public Dictionary<string, string> Errors { get; set; } = [];

    public static SmsMutationResponse Success(string? messageId = null)
    {
        return new SmsMutationResponse { IsSuccess = true, MessageId = messageId };
    }

    public static SmsMutationResponse Failure(string field, string message)
    {
        return new SmsMutationResponse
        {
            IsSuccess = false,
            Errors = new Dictionary<string, string> { [field] = message }
        };
    }
}
