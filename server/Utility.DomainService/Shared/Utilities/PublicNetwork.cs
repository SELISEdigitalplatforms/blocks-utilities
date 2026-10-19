using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace Utility.DomainService.Shared.Utilities
{
    /// <summary>
    /// Keeps outbound requests built from caller-supplied URLs on the public internet.
    /// </summary>
    /// <remarks>
    /// Checking the URL's host string is not enough: a public-looking name can resolve to
    /// <c>10.x</c>, <c>127.0.0.1</c> or the cloud metadata address <c>169.254.169.254</c>, which would
    /// let a caller reach cluster-internal services through the Worker (SSRF).
    /// <see cref="ConnectAsync"/> runs after DNS resolution, on every connection the handler opens
    /// (redirects included), and refuses the connection unless every resolved address is public.
    /// </remarks>
    public static class PublicNetwork
    {
        /// <summary>Largest resource <see cref="FetchAsync"/> will download.</summary>
        public const int MaxResourceBytes = 10 * 1024 * 1024;

        // One client for the process, so connections are pooled; every new connection, redirects
        // included, still goes through ConnectAsync. Static rather than a named factory client
        // because the Subscription module builds the PDF engines without this module's registrations.
        private static readonly HttpClient ResourceClient = new(new SocketsHttpHandler
        {
            ConnectCallback = ConnectAsync,
            UseProxy = false,
            MaxAutomaticRedirections = 5,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseContentBufferSize = MaxResourceBytes
        };

        /// <summary>
        /// Downloads an http(s) resource that caller-supplied content refers to, so a renderer never
        /// opens the connection itself. Null when <paramref name="location"/> is not an http(s) URL, the host is not public, or
        /// the download fails, times out or exceeds <see cref="MaxResourceBytes"/>.
        /// </summary>
        public static async Task<PublicResource?> FetchAsync(string? location, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }

            try
            {
                using var response = await ResourceClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                return new PublicResource(body, response.Content.Headers.ContentType?.ToString());
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return null;
            }
        }

        /// <summary>
        /// False for loopback, unspecified, private (RFC 1918 / unique-local), link-local and the
        /// other IPv6 ranges that are not routable on the public internet. Same ranges as
        /// Payment's <c>SafeHttpsUrl</c>; Payment references no other module, so it keeps its own copy.
        /// </summary>
        public static bool IsPublic(IPAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);

            if (IPAddress.IsLoopback(address) ||
                address.Equals(IPAddress.Any) ||
                address.Equals(IPAddress.IPv6Any))
            {
                return false;
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv6LinkLocal ||
                    address.IsIPv6SiteLocal ||
                    address.IsIPv6Multicast ||
                    address.IsIPv6Teredo)
                {
                    return false;
                }

                var ipv6 = address.GetAddressBytes();
                if ((ipv6[0] & 0xFE) == 0xFC) return false;
            }

            var bytes = address.MapToIPv4().GetAddressBytes();
            return !(bytes[0] == 10 || bytes[0] == 127 ||
                     bytes[0] == 169 && bytes[1] == 254 ||
                     bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                     bytes[0] == 192 && bytes[1] == 168);
        }

        /// <summary>
        /// A <see cref="SocketsHttpHandler.ConnectCallback"/> that connects only when every address
        /// the host resolves to is public. A host that mixes public and private answers is refused
        /// outright, since that is how DNS-rebinding attempts usually look.
        /// </summary>
        public static async ValueTask<Stream> ConnectAsync(
            SocketsHttpConnectionContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            var host = context.DnsEndPoint.Host;
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

            if (addresses.Length == 0 || !addresses.All(IsPublic))
            {
                throw new HttpRequestException($"Refusing to connect to '{host}': it does not resolve to a public address.");
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }

    /// <summary>A resource downloaded by <see cref="PublicNetwork.FetchAsync"/>.</summary>
    public sealed record PublicResource(byte[] Body, string? ContentType);
}
