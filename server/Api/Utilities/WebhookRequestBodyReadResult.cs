namespace Api.Utilities;

public sealed record WebhookRequestBodyReadResult(
    WebhookRequestBodyReadStatus Status,
    string RawBody)
{
    public static WebhookRequestBodyReadResult Success(
        string rawBody) =>
        new(
            WebhookRequestBodyReadStatus.Success,
            rawBody);

    public static WebhookRequestBodyReadResult Malformed() =>
        new(
            WebhookRequestBodyReadStatus.Malformed,
            string.Empty);

    public static WebhookRequestBodyReadResult TooLarge() =>
        new(
            WebhookRequestBodyReadStatus.TooLarge,
            string.Empty);
}
