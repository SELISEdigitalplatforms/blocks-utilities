using Payment.DomainService.Enums;

namespace Payment.DomainService.Responses;

public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public ApiError? Error { get; init; }
    public ResponseMetadata Meta { get; init; } = new();

    public static ApiResponse<T> Ok(T data, string correlationId, bool replayed = false) => new()
    {
        Success = true,
        Data = data,
        Meta = new ResponseMetadata { CorrelationId = correlationId, Replayed = replayed }
    };

    public static ApiResponse<T> Fail(string code, string message, string correlationId, Dictionary<string, string[]>? fields = null) => new()
    {
        Success = false,
        Error = new ApiError { Code = code, Message = message, Fields = fields, TraceId = correlationId },
        Meta = new ResponseMetadata { CorrelationId = correlationId }
    };
}
