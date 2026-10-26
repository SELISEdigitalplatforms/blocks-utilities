namespace Utility.DomainService.Drive;

/// <summary>
/// Why a drive operation did not succeed. The Api maps each kind to a status code and error code.
/// </summary>
public enum DriveFailureKind
{
    None = 0,

    /// <summary>The request itself was malformed; FluentValidation said so.</summary>
    InvalidRequest,

    /// <summary>No usable connection for this user in this project. The caller should reconnect.</summary>
    NotConnected,

    /// <summary>The user's organization requires an admin to approve the app first.</summary>
    AdminConsentRequired,

    /// <summary>Graph kept answering 429/503 past the retry budget. Carries a retry-after hint.</summary>
    Throttled,

    ItemNotFound,

    NameConflict,

    FileTooLarge,

    /// <summary>Graph answered with something we cannot act on (5xx after retries, unexpected body).</summary>
    ProviderError,

    /// <summary>Blocks Storage refused or failed the read or write.</summary>
    StorageError,

    /// <summary>Entra client id/secret are not configured. Deployment problem, not a user one.</summary>
    NotConfigured
}

/// <summary>
/// Result-style return for drive operations, in place of exceptions for expected failures.
/// </summary>
public sealed class DriveResult<T>
{
    private DriveResult(bool isSuccess, T? value, DriveFailureKind failureKind, string? errorMessage, int? retryAfterSeconds)
    {
        IsSuccess = isSuccess;
        Value = value;
        FailureKind = failureKind;
        ErrorMessage = errorMessage;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public DriveFailureKind FailureKind { get; }

    public string? ErrorMessage { get; }

    /// <summary>Only set for <see cref="DriveFailureKind.Throttled"/>.</summary>
    public int? RetryAfterSeconds { get; }

    public static DriveResult<T> Ok(T value) => new(true, value, DriveFailureKind.None, null, null);

    public static DriveResult<T> Fail(DriveFailureKind kind, string message, int? retryAfterSeconds = null) =>
        new(false, default, kind, message, retryAfterSeconds);

    /// <summary>Carries another result's failure across a type boundary.</summary>
    public static DriveResult<T> From<TOther>(DriveResult<TOther> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Fail(other.FailureKind, other.ErrorMessage ?? "The drive operation failed.", other.RetryAfterSeconds);
    }
}
