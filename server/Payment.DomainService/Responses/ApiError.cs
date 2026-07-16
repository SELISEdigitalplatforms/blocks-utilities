using Payment.DomainService.Enums;

namespace Payment.DomainService.Responses;

public sealed class ApiError
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public Dictionary<string, string[]>? Fields { get; init; }
    public string TraceId { get; init; } = string.Empty;
}
