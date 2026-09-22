using System.Net;
using System.Text.Json;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Utility.DomainService.Geolocation;
using Utility.DomainService.Geolocation.service;

namespace XUnitTest.Geolocation
{
    /// <summary>
    /// Guards the two ways IP geolocation goes wrong in a way a customer notices: a fabricated
    /// location that reads like a real one, and a provider bill or rate-limit ban run up by a
    /// caller that asked for ten addresses.
    /// </summary>
    /// <remarks>
    /// The provider delay is configured to 1ms throughout. The gate itself is still exercised -
    /// what is not exercised is waiting a real second for it, which would make this file take
    /// minutes.
    /// </remarks>
    public sealed class GeolocationRepositoryTests
    {
        private const string ApiUrlWithKeyInQuery =
            "https://provider.example/v1/?api_key={apiKey}&ip_address={ip}";

        private const string ApiUrlWithoutKeyInQuery =
            "https://provider.example/{ip}/json";

        private readonly Mock<ICacheClient> _cacheClient = new();
        private readonly Mock<IVault> _vault = new();

        public GeolocationRepositoryTests()
        {
            _cacheClient
                .Setup(client => client.GetStringValueAsync(It.IsAny<string>()))
                .ReturnsAsync((string?)null);
            _cacheClient
                .Setup(client => client.AddStringValueAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<long>()))
                .ReturnsAsync(true);
            _vault
                .Setup(vault => vault.ProcessSecretsAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(new Dictionary<string, string>());
        }

        [Fact]
        public async Task An_address_that_is_not_an_ip_address_is_never_sent_to_the_provider()
        {
            var calls = 0;
            var repository = CreateRepository(
                _ =>
                {
                    calls++;
                    return Json("{}");
                },
                apiUrl: ApiUrlWithKeyInQuery);

            var result = await repository.ResolveIpToLocationAsync("not-an-ip");

            result.Should().BeNull(
                because: "an unparseable address has no location, and guessing one would put a "
                    + "fabricated country into whatever audit record consumes this");
            calls.Should().Be(0,
                because: "caller-supplied text is interpolated into the provider URL, so it must "
                    + "be rejected before the call rather than escaped on the way out");
        }

        [Fact]
        public async Task A_failed_provider_call_resolves_to_nothing_rather_than_to_a_placeholder()
        {
            var repository = CreateRepository(
                _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
                apiUrl: ApiUrlWithKeyInQuery);

            var result = await repository.ResolveIpToLocationAsync("8.8.8.8");

            result.Should().BeNull(
                because: "a rate-limited provider must not be reported as a successful lookup of "
                    + "an 'Unknown' country - the caller cannot tell that apart from a real answer");
        }

        [Fact]
        public async Task A_failed_provider_call_is_not_cached()
        {
            var repository = CreateRepository(
                _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
                apiUrl: ApiUrlWithKeyInQuery);

            await repository.ResolveIpToLocationAsync("8.8.8.8");

            _cacheClient.Verify(
                client => client.AddStringValueAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<long>()),
                Times.Never,
                failMessage: "caching a failure serves one bad minute at the provider back for the "
                    + "whole TTL, which is how a transient outage becomes a lasting one");
        }

        [Fact]
        public async Task A_provider_that_cannot_be_reached_at_all_resolves_to_nothing()
        {
            var repository = CreateRepository(
                _ => throw new HttpRequestException("connection refused"),
                apiUrl: ApiUrlWithKeyInQuery);

            var result = await repository.ResolveIpToLocationAsync("8.8.8.8");

            result.Should().BeNull(
                because: "a transport failure must degrade to 'no location' rather than escape as "
                    + "a 500 from an endpoint whose contract can already say it could not locate");
        }

        [Fact]
        public async Task A_payload_that_is_not_json_resolves_to_nothing()
        {
            var repository = CreateRepository(
                _ => Json("not json at all"),
                apiUrl: ApiUrlWithKeyInQuery);

            var result = await repository.ResolveIpToLocationAsync("8.8.8.8");

            result.Should().BeNull(
                because: "a provider that changed shape, or an error page served with a 200, must "
                    + "not be mapped into a record that looks like a location");
        }

        [Fact]
        public async Task A_lookup_with_no_provider_url_configured_resolves_to_nothing()
        {
            var repository = CreateRepository(apiUrl: null);

            var result = await repository.ResolveIpToLocationAsync("8.8.8.8");

            result.Should().BeNull(
                because: "an unconfigured deployment should report that it cannot locate, not "
                    + "hand back a synthesized answer that hides the missing configuration");
        }

        [Fact]
        public async Task A_successful_lookup_is_mapped_and_cached_with_the_configured_ttl()
        {
            var repository = CreateRepository(
                _ => Json(
                    """
                    {
                      "country_code": "CH",
                      "country": "Switzerland",
                      "continent_code": "EU",
                      "continent": "Europe",
                      "city": "Zurich",
                      "region": "Zurich",
                      "region_iso_code": "ZH",
                      "latitude": 47.3769,
                      "longitude": 8.5417,
                      "connection": { "isp_name": "Init7" },
                      "flag": { "png": "https://f/ch.png", "svg": "https://f/ch.svg" }
                    }
                    """),
                apiUrl: ApiUrlWithKeyInQuery,
                cacheSeconds: "300");

            var result = await repository.ResolveIpToLocationAsync("8.8.8.8");

            result.Should().NotBeNull();
            result!.CountryCode.Should().Be("CH");
            result.CountryName.Should().Be("Switzerland");
            result.City.Should().Be("Zurich");
            result.IspName.Should().Be("Init7",
                because: "providers nest the ISP under 'connection', and losing it silently means "
                    + "an audit record that cannot answer who the visitor connected through");
            result.CountryFlagPngUrl.Should().Be("https://f/ch.png");
            result.Latitude.Should().BeApproximately(47.3769, 0.0001);
            result.StartIpNumber.Should().Be(134744072,
                because: "callers range-search on the numeric form, so a wrong conversion silently "
                    + "moves an address into another block");

            _cacheClient.Verify(
                client => client.AddStringValueAsync(
                    "ip_lookup_8.8.8.8",
                    It.IsAny<string>(),
                    300),
                Times.Once,
                failMessage: "the TTL is the knob that bounds provider spend against staleness, so "
                    + "it has to be the configured value and not a hard-coded hour");
        }

        [Fact]
        public async Task The_cache_ttl_falls_back_to_five_minutes_when_it_is_not_configured()
        {
            var repository = CreateRepository(
                _ => Json("""{"country_code":"CH"}"""),
                apiUrl: ApiUrlWithKeyInQuery,
                cacheSeconds: null);

            await repository.ResolveIpToLocationAsync("8.8.8.8");

            _cacheClient.Verify(
                client => client.AddStringValueAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    300),
                Times.Once,
                failMessage: "an unset TTL must not mean 'cache forever' or 'do not cache'; both "
                    + "extremes show up as a support ticket rather than as an error");
        }

        [Fact]
        public async Task A_nonsense_cache_ttl_falls_back_rather_than_disabling_the_cache()
        {
            var repository = CreateRepository(
                _ => Json("""{"country_code":"CH"}"""),
                apiUrl: ApiUrlWithKeyInQuery,
                cacheSeconds: "-1");

            await repository.ResolveIpToLocationAsync("8.8.8.8");

            _cacheClient.Verify(
                client => client.AddStringValueAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    300),
                Times.Once,
                failMessage: "a typo in configuration must not silently turn off caching and put "
                    + "every lookup on the provider's meter");
        }

        [Fact]
        public async Task A_cached_lookup_is_served_without_calling_the_provider()
        {
            var cached = new IpLookup { StartIp = "8.8.8.8", CountryCode = "US" };
            var calls = 0;

            _cacheClient
                .Setup(client => client.GetStringValueAsync("ip_lookup_8.8.8.8"))
                .ReturnsAsync(JsonSerializer.Serialize(cached));

            var repository = CreateRepository(
                _ =>
                {
                    calls++;
                    return Json("{}");
                },
                apiUrl: ApiUrlWithKeyInQuery);

            var result = await repository.ResolveIpToLocationAsync("8.8.8.8");

            result!.CountryCode.Should().Be("US");
            calls.Should().Be(0,
                because: "the cache exists to keep repeat visitors off the provider's meter; a "
                    + "miss that still calls out defeats the only reason it is there");
        }

        [Fact]
        public async Task An_unreachable_cache_degrades_to_a_provider_call_rather_than_failing()
        {
            _cacheClient
                .Setup(client => client.GetStringValueAsync(It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("cache is down"));

            var repository = CreateRepository(
                _ => Json("""{"country_code":"CH"}"""),
                apiUrl: ApiUrlWithKeyInQuery);

            var result = await repository.ResolveIpToLocationAsync("8.8.8.8");

            result!.CountryCode.Should().Be("CH",
                because: "the cache is an optimisation, so losing it should cost latency and money "
                    + "rather than the feature");
        }

        [Fact]
        public async Task The_provider_key_comes_from_the_vault()
        {
            _vault
                .Setup(vault => vault.ProcessSecretsAsync(
                    It.Is<List<string>>(names => names.Contains("GeolocationApiKey"))))
                .ReturnsAsync(new Dictionary<string, string>
                {
                    ["GeolocationApiKey"] = "vault-key"
                });

            string? requestedUrl = null;
            var repository = CreateRepository(
                request =>
                {
                    requestedUrl = request.RequestUri!.ToString();
                    return Json("""{"country_code":"CH"}""");
                },
                apiUrl: ApiUrlWithKeyInQuery,
                configuredApiKey: "appsettings-key");

            await repository.ResolveIpToLocationAsync("8.8.8.8");

            requestedUrl.Should().Contain("api_key=vault-key",
                because: "the vault is the rotatable source; preferring a committed key would mean "
                    + "a rotation that appears to work and silently keeps using the old secret");
            requestedUrl.Should().NotContain("appsettings-key");
        }

        [Fact]
        public async Task The_vault_is_read_once_and_reused_across_lookups()
        {
            _vault
                .Setup(vault => vault.ProcessSecretsAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(new Dictionary<string, string>
                {
                    ["GeolocationApiKey"] = "vault-key"
                });

            var repository = CreateRepository(
                _ => Json("""{"country_code":"CH"}"""),
                apiUrl: ApiUrlWithKeyInQuery);

            await repository.ResolveIpToLocationAsync("8.8.8.8");
            await repository.ResolveIpToLocationAsync("1.1.1.1");

            _vault.Verify(
                vault => vault.ProcessSecretsAsync(It.IsAny<List<string>>()),
                Times.Once,
                failMessage: "a vault round trip per lookup costs more than the lookup and adds a "
                    + "second remote dependency to every call");
        }

        [Fact]
        public async Task A_key_missing_from_the_vault_falls_back_to_configuration()
        {
            string? requestedUrl = null;
            var repository = CreateRepository(
                request =>
                {
                    requestedUrl = request.RequestUri!.ToString();
                    return Json("""{"country_code":"CH"}""");
                },
                apiUrl: ApiUrlWithKeyInQuery,
                configuredApiKey: "appsettings-key");

            await repository.ResolveIpToLocationAsync("8.8.8.8");

            requestedUrl.Should().Contain("api_key=appsettings-key",
                because: "a developer without vault access still has to be able to run the service "
                    + "against a provider");
        }

        [Fact]
        public async Task A_vault_that_throws_falls_back_to_configuration_rather_than_failing()
        {
            _vault
                .Setup(vault => vault.ProcessSecretsAsync(It.IsAny<List<string>>()))
                .ThrowsAsync(new InvalidOperationException("vault unreachable"));

            var repository = CreateRepository(
                _ => Json("""{"country_code":"CH"}"""),
                apiUrl: ApiUrlWithKeyInQuery,
                configuredApiKey: "appsettings-key");

            var result = await repository.ResolveIpToLocationAsync("8.8.8.8");

            result!.CountryCode.Should().Be("CH",
                because: "an unreachable vault must not take geolocation down while a usable key "
                    + "is sitting in configuration");
        }

        [Fact]
        public async Task A_vault_secret_is_looked_up_under_the_configured_name()
        {
            var repository = CreateRepository(
                _ => Json("""{"country_code":"CH"}"""),
                apiUrl: ApiUrlWithKeyInQuery,
                secretName: "geolocation-provider-key");

            await repository.ResolveIpToLocationAsync("8.8.8.8");

            _vault.Verify(
                vault => vault.ProcessSecretsAsync(
                    It.Is<List<string>>(names => names.Contains("geolocation-provider-key"))),
                Times.Once,
                failMessage: "vault naming differs per environment, so the secret name has to be "
                    + "configurable rather than pinned to the configuration key");
        }

        [Fact]
        public async Task A_provider_that_authenticates_by_header_receives_the_key_as_a_header()
        {
            _vault
                .Setup(vault => vault.ProcessSecretsAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(new Dictionary<string, string>
                {
                    ["GeolocationApiKey"] = "vault-key"
                });

            string? headerValue = null;
            var repository = CreateRepository(
                request =>
                {
                    headerValue = request.Headers.TryGetValues("X-API-Key", out var values)
                        ? values.First()
                        : null;
                    return Json("""{"country_code":"CH"}""");
                },
                apiUrl: ApiUrlWithoutKeyInQuery);

            await repository.ResolveIpToLocationAsync("8.8.8.8");

            headerValue.Should().Be("vault-key",
                because: "a URL without an {apiKey} placeholder means the provider expects the key "
                    + "in a header, and sending it nowhere reads as an unauthenticated caller");
        }

        [Fact]
        public async Task A_keyless_provider_is_called_without_an_api_key_header()
        {
            var sawHeader = true;
            var repository = CreateRepository(
                request =>
                {
                    sawHeader = request.Headers.Contains("X-API-Key");
                    return Json("""{"country_code":"CH"}""");
                },
                apiUrl: ApiUrlWithoutKeyInQuery);

            var result = await repository.ResolveIpToLocationAsync("8.8.8.8");

            sawHeader.Should().BeFalse(
                because: "ip-api.com and friends need no key, and sending an empty one is a header "
                    + "some providers reject outright");
            result.Should().NotBeNull();
        }

        [Fact]
        public async Task A_bulk_lookup_drops_the_addresses_that_could_not_be_resolved()
        {
            var repository = CreateRepository(
                request => request.RequestUri!.ToString().Contains("8.8.8.8", StringComparison.Ordinal)
                    ? Json("""{"country_code":"CH"}""")
                    : new HttpResponseMessage(HttpStatusCode.BadRequest),
                apiUrl: ApiUrlWithKeyInQuery);

            var result = await repository.ResolveMultipleIpsToCountryAsync(
                ["8.8.8.8", "1.1.1.1", "not-an-ip"]);

            result.Should().HaveCount(1,
                because: "an entry that could not be resolved has to be absent rather than present "
                    + "and empty, or the caller counts a failure as a located address");
            result[0].CountryCode.Should().Be("CH");
        }

        [Fact]
        public async Task A_bulk_lookup_calls_the_provider_once_per_distinct_address()
        {
            var cache = new Dictionary<string, string>();

            _cacheClient
                .Setup(client => client.GetStringValueAsync(It.IsAny<string>()))
                .ReturnsAsync((string key) => cache.GetValueOrDefault(key));
            _cacheClient
                .Setup(client => client.AddStringValueAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<long>()))
                .ReturnsAsync((string key, string value, long _) =>
                {
                    cache[key] = value;
                    return true;
                });

            var calls = 0;
            var repository = CreateRepository(
                _ =>
                {
                    calls++;
                    return Json("""{"country_code":"CH"}""");
                },
                apiUrl: ApiUrlWithKeyInQuery);

            var result = await repository.ResolveMultipleIpsToCountryAsync(
                ["8.8.8.8", "8.8.8.8", "8.8.8.8"]);

            calls.Should().Be(1,
                because: "a repeated address inside one request must be served from the cache; "
                    + "paying the provider three times for one answer is the bill this guards");
            result.Should().HaveCount(3,
                because: "the caller asked about three entries and is entitled to an answer for "
                    + "each of them");
        }

        [Fact]
        public async Task A_bulk_lookup_preserves_the_order_it_was_asked_in()
        {
            var repository = CreateRepository(
                request => Json(
                    request.RequestUri!.ToString().Contains("8.8.8.8", StringComparison.Ordinal)
                        ? """{"country_code":"US"}"""
                        : """{"country_code":"AU"}"""),
                apiUrl: ApiUrlWithKeyInQuery);

            var result = await repository.ResolveMultipleIpsToCountryAsync(["8.8.8.8", "1.1.1.1"]);

            result.Select(lookup => lookup.CountryCode).Should().Equal(["US", "AU"],
                because: "callers line these up against the addresses they sent, so reordering "
                    + "them attributes one visitor's location to another");
        }

        [Fact]
        public async Task A_cancelled_lookup_stops_rather_than_reporting_no_location()
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            var repository = CreateRepository(
                _ => Json("""{"country_code":"CH"}"""),
                apiUrl: ApiUrlWithKeyInQuery);

            var act = () => repository.ResolveIpToLocationAsync("8.8.8.8", cancellation.Token);

            await act.Should().ThrowAsync<OperationCanceledException>(
                because: "a caller that has gone away must not be reported as an address that "
                    + "could not be located, which would be indistinguishable from a real failure");
        }

        [Fact]
        public async Task A_null_bulk_input_is_an_empty_result()
        {
            var repository = CreateRepository(apiUrl: ApiUrlWithKeyInQuery);

            var result = await repository.ResolveMultipleIpsToCountryAsync(null!);

            result.Should().BeEmpty();
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            };

        private GeolocationRepository CreateRepository(
            Func<HttpRequestMessage, HttpResponseMessage>? requestHandler = null,
            string? apiUrl = ApiUrlWithKeyInQuery,
            string? configuredApiKey = null,
            string? secretName = null,
            string? cacheSeconds = "300")
        {
            var messageHandler = new Mock<HttpMessageHandler>();

            messageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
                    requestHandler?.Invoke(request) ?? Json("{}"));

            var httpClientFactory = new Mock<IHttpClientFactory>();

            httpClientFactory
                .Setup(factory => factory.CreateClient(It.IsAny<string>()))
                .Returns(() => new HttpClient(messageHandler.Object));

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GeolocationApiUrl"] = apiUrl,
                    ["GeolocationApiKey"] = configuredApiKey,
                    ["GeolocationApiKeySecretName"] = secretName,
                    ["GeolocationCacheSeconds"] = cacheSeconds,

                    // The gate is what is under test, not the wait. One millisecond exercises the
                    // same code path as the configured second.
                    ["GeolocationProviderDelayMilliseconds"] = "1"
                })
                .Build();

            return new GeolocationRepository(
                _cacheClient.Object,
                httpClientFactory.Object,
                configuration,
                _vault.Object,
                NullLogger<GeolocationRepository>.Instance);
        }
    }
}
