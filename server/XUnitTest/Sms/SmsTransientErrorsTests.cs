using System.Net;
using System.Net.Http;
using FluentAssertions;
using Sms.DomainService.Utilities;
using Telnyx;
using Twilio.Exceptions;

namespace XUnitTest.Sms;

public class SmsTransientErrorsTests
{
    public static TheoryData<Exception> Transient() =>
    [
        new ApiException(20429, 429, "Too Many Requests", null),
        new ApiException(20500, 503, "Service Unavailable", null),
        new ApiConnectionException("connection reset"),
        new TelnyxException(HttpStatusCode.TooManyRequests, [], "rate limited"),
        new TelnyxException(HttpStatusCode.BadGateway, [], "bad gateway"),
        new HttpRequestException("socket closed"),
        new TimeoutException(),
        new TaskCanceledException("HttpClient.Timeout elapsed")
    ];

    public static TheoryData<Exception> Permanent() =>
    [
        new ApiException(21211, 400, "Invalid 'To' Phone Number", null),
        new ApiException(20003, 401, "Authenticate", null),
        new TelnyxException(HttpStatusCode.UnprocessableEntity, [], "invalid destination"),
        new InvalidOperationException("bug")
    ];

    [Theory]
    [MemberData(nameof(Transient))]
    public void ProviderHiccupsAreRetried(Exception exception) =>
        SmsTransientErrors.IsTransient(exception).Should().BeTrue();

    [Theory]
    [MemberData(nameof(Permanent))]
    public void RequestProblemsAreNot(Exception exception) =>
        SmsTransientErrors.IsTransient(exception).Should().BeFalse();
}
