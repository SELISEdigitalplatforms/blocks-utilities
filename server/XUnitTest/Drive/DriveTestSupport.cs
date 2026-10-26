using System.Net;
using System.Text;
using Blocks.Genesis;
using Microsoft.Extensions.Options;
using Moq;
using Utility.DomainService.Drive;
using XUnitTest.Payment;

namespace XUnitTest.Drive;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that answers from a script: each entry matches a method
/// and a URL fragment and returns a canned response, recording every request it saw.
/// </summary>
/// <remarks>
/// Entra and Graph are never called by these tests; what is asserted is the exact request the
/// code sends and how it reacts to each response, which is where every integration bug lives.
/// </remarks>
internal sealed class ScriptedHttpHandler : HttpMessageHandler
{
    private readonly List<(Func<HttpRequestMessage, bool> Match, Func<HttpRequestMessage, HttpResponseMessage> Respond, bool Once)> _rules = [];

    public List<RecordedRequest> Requests { get; } = [];

    public ScriptedHttpHandler On(HttpMethod method, string urlContains, Func<HttpRequestMessage, HttpResponseMessage> respond, bool once = false)
    {
        _rules.Add((r => r.Method == method && r.RequestUri!.ToString().Contains(urlContains, StringComparison.Ordinal), respond, once));
        return this;
    }

    public ScriptedHttpHandler On(HttpMethod method, string urlContains, HttpStatusCode status, string json, bool once = false) =>
        On(method, urlContains, _ => Json(status, json), once);

    /// <summary>Answers the same match with each response in turn, then keeps repeating the last.</summary>
    public ScriptedHttpHandler Sequence(HttpMethod method, string urlContains, params HttpResponseMessage[] responses)
    {
        var index = 0;
        return On(method, urlContains, _ => responses[Math.Min(index++, responses.Length - 1)]);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Throttled(int retryAfterSeconds)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterSeconds));
        return response;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        // AbsoluteUri keeps percent-encoding, which is what Graph actually receives.
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsoluteUri, body, request));

        for (var i = 0; i < _rules.Count; i++)
        {
            var rule = _rules[i];
            if (rule.Match(request))
            {
                if (rule.Once)
                {
                    _rules.RemoveAt(i);
                }

                return rule.Respond(request);
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotImplemented)
        {
            Content = new StringContent($"no script for {request.Method} {request.RequestUri}")
        };
    }
}

internal sealed record RecordedRequest(HttpMethod Method, string Url, string? Body, HttpRequestMessage Message);

internal static class DriveTestSupport
{
    public const string ProjectKey = "project-1";
    public const string UserId = "user-1";

    /// <summary>A 32-byte key, base64, as the real one in Blocks Secrets would be.</summary>
    public const string Key = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    public static IOptions<DriveOptions> Options(Action<DriveOptions>? configure = null)
    {
        var options = new DriveOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUrl = "https://utilities.test/api/drive/connections/callback",
            TokenEncryptionKey = Key,
            MaxRetries = 3,
            DefaultRetryAfterSeconds = 1,
        };
        configure?.Invoke(options);
        return Microsoft.Extensions.Options.Options.Create(options);
    }

    public static IHttpClientFactory Factory(ScriptedHttpHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler));
        return factory.Object;
    }

    public static ControlledTimeProvider Clock() => new(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The caller every request below comes from.</summary>
    public static void SetCaller(string projectKey = ProjectKey, string userId = UserId) =>
        BlocksContext.SetContext(BlocksContext.Create(
            projectKey, null, userId, true, null, "org-1",
            DateTime.UtcNow.AddHours(1), null, null, null, null, null, null, null));

    public static void ClearCaller() => BlocksContext.SetContext(null);

    /// <summary>A token response as Entra sends it, with an id_token carrying the given claims.</summary>
    public static string TokenJson(string accessToken = "access-1", string? refreshToken = "refresh-1", int expiresIn = 3600, string tid = "tenant-abc", string name = "Ada Lovelace", string email = "ada@example.com")
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{{\"tid\":\"{tid}\",\"name\":\"{name}\",\"preferred_username\":\"{email}\"}}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var refresh = refreshToken is null ? string.Empty : $",\"refresh_token\":\"{refreshToken}\"";
        return $"{{\"token_type\":\"Bearer\",\"scope\":\"Files.ReadWrite.All\",\"expires_in\":{expiresIn},\"access_token\":\"{accessToken}\"{refresh},\"id_token\":\"h.{payload}.s\"}}";
    }

    public static string Item(string id, string name, bool folder = false, long size = 0, string driveId = "drive-1", string? parentId = "root-id") =>
        $"{{\"id\":\"{id}\",\"name\":\"{name}\",\"size\":{size},\"webUrl\":\"https://contoso.sharepoint.com/{name}\"," +
        $"\"createdDateTime\":\"2026-10-01T10:00:00Z\",\"lastModifiedDateTime\":\"2026-10-02T10:00:00Z\"," +
        (folder ? "\"folder\":{\"childCount\":1}," : "\"file\":{\"mimeType\":\"application/pdf\"},") +
        $"\"parentReference\":{{\"driveId\":\"{driveId}\",\"id\":\"{parentId}\"}}}}";
}
