using System.Text.Json.Serialization;

namespace Sms.DomainService.Requests;

public class TwilioSmsStatusCallbackRequest
{
    public string? MessageSid { get; set; }
    public string? SmsSid { get; set; }
    public string? MessageStatus { get; set; }
    public string? SmsStatus { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? To { get; set; }
    public string? From { get; set; }
}

public class TelnyxSmsStatusCallbackRequest
{
    [JsonPropertyName("data")]
    public TelnyxSmsStatusData? Data { get; set; }
}

public class TelnyxSmsStatusData
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("event_type")]
    public string? EventType { get; set; }

    [JsonPropertyName("payload")]
    public TelnyxSmsStatusPayload? Payload { get; set; }
}

public class TelnyxSmsStatusPayload
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("to")]
    public TelnyxPhoneNumber? To { get; set; }

    [JsonPropertyName("from")]
    public TelnyxPhoneNumber? From { get; set; }

    [JsonPropertyName("errors")]
    public List<TelnyxStatusError> Errors { get; set; } = [];
}

public class TelnyxPhoneNumber
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public class TelnyxStatusError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}
