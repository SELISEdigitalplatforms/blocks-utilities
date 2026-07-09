using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Mail.DomainService.Mails.Services.DeliveryTracking;

public sealed class AmazonSnsMessageVerifier : IAmazonSnsMessageVerifier
{
    private readonly IHttpClientFactory _httpClientFactory;

    public AmazonSnsMessageVerifier(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<bool> VerifyAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;

        if (!TryGetString(root, "Signature", out var signature) ||
            !TryGetString(root, "SignatureVersion", out var signatureVersion) ||
            !TryGetString(root, "SigningCertURL", out var certificateUrl) ||
            !IsTrustedCertificateUrl(certificateUrl))
        {
            return false;
        }

        var canonicalMessage = BuildCanonicalMessage(root);
        if (canonicalMessage == null)
        {
            return false;
        }

        var certificatePem = await _httpClientFactory.CreateClient()
            .GetStringAsync(certificateUrl, cancellationToken);
        using var certificate = X509Certificate2.CreateFromPem(certificatePem);
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        if (!chain.Build(certificate))
        {
            return false;
        }

        using var publicKey = certificate.GetRSAPublicKey();
        if (publicKey == null)
        {
            return false;
        }

        var hashAlgorithm = signatureVersion switch
        {
            "1" => HashAlgorithmName.SHA1,
            "2" => HashAlgorithmName.SHA256,
            _ => default
        };

        if (hashAlgorithm == default)
        {
            return false;
        }

        try
        {
            return publicKey.VerifyData(
                Encoding.UTF8.GetBytes(canonicalMessage),
                Convert.FromBase64String(signature),
                hashAlgorithm,
                RSASignaturePadding.Pkcs1);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static string? BuildCanonicalMessage(JsonElement root)
    {
        if (!TryGetString(root, "Type", out var type))
        {
            return null;
        }

        var fields = type switch
        {
            "Notification" => new[] { "Message", "MessageId", "Subject", "Timestamp", "TopicArn", "Type" },
            "SubscriptionConfirmation" or "UnsubscribeConfirmation" =>
                new[] { "Message", "MessageId", "SubscribeURL", "Timestamp", "Token", "TopicArn", "Type" },
            _ => []
        };

        if (fields.Length == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var field in fields)
        {
            if (!TryGetString(root, field, out var value))
            {
                if (field == "Subject")
                {
                    continue;
                }

                return null;
            }

            builder.Append(field).Append('\n').Append(value).Append('\n');
        }

        return builder.ToString();
    }

    internal static bool IsTrustedCertificateUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttps &&
               IsAmazonSnsHost(uri.Host) &&
               uri.AbsolutePath.Contains("SimpleNotificationService-", StringComparison.Ordinal) &&
               uri.AbsolutePath.EndsWith(".pem", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsAmazonSnsHost(string host)
    {
        return host.Equals("sns.amazonaws.com", StringComparison.OrdinalIgnoreCase) ||
               (host.StartsWith("sns.", StringComparison.OrdinalIgnoreCase) &&
                (host.EndsWith(".amazonaws.com", StringComparison.OrdinalIgnoreCase) ||
                 host.EndsWith(".amazonaws.com.cn", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(value = property.GetString() ?? string.Empty);
    }
}
