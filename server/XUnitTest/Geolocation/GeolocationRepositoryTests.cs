using System.Net;
using System.Text.Json;
using Blocks.Genesis;
using FluentAssertions;
using Blocks.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        private readonly Mock<ISecretService> _secretService = new();

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
        public async Task The_provider_key_comes_from_blocks_secrets_when_a_secret_id_is_configured()
        {
            _secretService
                .Setup(secrets => secrets.GetValueAsync("secret-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync("managed-key");
            _vault
                .Setup(vault => vault.ProcessSecretsAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(new Dictionary<string, string> { ["GeolocationApiKey"] = "vault-key" });

            string? requestedUrl = null;
            var repository = CreateRepository(
                request =>
                {
                    requestedUrl = request.RequestUri!.ToString();
                    return Json("""{"country_code":"CH"}""");
                },
                apiUrl: ApiUrlWithKeyInQuery,
                configuredApiKey: "appsettings-key",
                secretId: "secret-1");

            await repository.ResolveIpToLocationAsync("8.8.8.8");

            requestedUrl.Should().Contain("api_key=managed-key",
                because: "a secret id names the managed store, which is the one an operator "
                    + "rotates and the only one whose reads are audited");
            requestedUrl.Should().NotContain("vault-key");
            requestedUrl.Should().NotContain("appsettings-key");
        }

        [Fact]
        public async Task Blocks_secrets_is_not_consulted_when_no_secret_id_is_configured()
        {
            var repository = CreateRepository(
                _ => Json("""{"country_code":"CH"}"""),
                apiUrl: ApiUrlWithKeyInQuery,
                secretId: null);

            await repository.ResolveIpToLocationAsync("8.8.8.8");

            _secretService.Verify(
                secrets => secrets.GetValueAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never,
                failMessage: "a deployment that has not adopted the managed store must not pay a "
                    + "secret-store round trip, nor an audit row, on every key resolution");
        }

        [Fact]
        public async Task A_secret_store_that_refuses_falls_back_to_the_vault()
        {
            // What a caller with no BlocksContext hits, and what a locked or deleted secret raises.
            _secretService
                .Setup(secrets => secrets.GetValueAsync("secret-1", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("no blocks context"));
            _vault
                .Setup(vault => vault.ProcessSecretsAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(new Dictionary<string, string> { ["GeolocationApiKey"] = "vault-key" });

            string? requestedUrl = null;
            var repository = CreateRepository(
                request =>
                {
                    requestedUrl = request.RequestUri!.ToString();
                    return Json("""{"country_code":"CH"}""");
                },
                apiUrl: ApiUrlWithKeyInQuery,
                secretId: "secret-1");

            var result = await repository.ResolveIpToLocationAsync("8.8.8.8");

            result!.CountryCode.Should().Be("CH");
            requestedUrl.Should().Contain("api_key=vault-key",
                because: "an unreachable secret store must not take geolocation down while a "
                    + "usable key sits one source further along");
        }

        [Fact]
        public async Task A_secret_that_holds_no_value_falls_back_to_the_vault()
        {
            _secretService
                .Setup(secrets => secrets.GetValueAsync("secret-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(string.Empty);
            _vault
                .Setup(vault => vault.ProcessSecretsAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(new Dictionary<string, string> { ["GeolocationApiKey"] = "vault-key" });

            string? requestedUrl = null;
            var repository = CreateRepository(
                request =>
                {
                    requestedUrl = request.RequestUri!.ToString();
                    return Json("""{"country_code":"CH"}""");
                },
                apiUrl: ApiUrlWithKeyInQuery,
                secretId: "secret-1");

            await repository.ResolveIpToLocationAsync("8.8.8.8");

            requestedUrl.Should().Contain("api_key=vault-key",
                because: "an empty secret is a half-provisioned one, and authenticating with a "
                    + "blank key just fails at the provider with a less obvious message");
        }

        [Fact]
        public async Task The_secret_store_is_read_once_and_reused_across_lookups()
        {
            _secretService
                .Setup(secrets => secrets.GetValueAsync("secret-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync("managed-key");

            var repository = CreateRepository(
                _ => Json("""{"country_code":"CH"}"""),
                apiUrl: ApiUrlWithKeyInQuery,
                secretId: "secret-1");

            await repository.ResolveIpToLocationAsync("8.8.8.8");
            await repository.ResolveIpToLocationAsync("1.1.1.1");

            _secretService.Verify(
                secrets => secrets.GetValueAsync("secret-1", It.IsAny<CancellationToken>()),
                Times.Once,
                failMessage: "every read of a secret value is audited, so a read per lookup fills "
                    + "the audit trail with noise and puts a second remote call on every request");
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

        /// <summary>
        /// ip-api.com, from its documented field set.
        /// </summary>
        /// <remarks>
        /// The trap this pins: ip-api.com puts the subdivision's ISO code in <c>region</c> and its
        /// name in <c>regionName</c>, the opposite of every other provider here. Consulting
        /// <c>region</c> first files "CA" as the region name.
        /// </remarks>
        [Fact]
        public async Task An_ip_api_com_payload_maps_the_region_name_rather_than_its_code()
        {
            var repository = CreateRepository(
                _ => Json(
                    """
                    {
                      "status": "success",
                      "country": "United States",
                      "countryCode": "US",
                      "region": "CA",
                      "regionName": "California",
                      "city": "Mountain View",
                      "zip": "94043",
                      "lat": 37.4056,
                      "lon": -122.0775,
                      "timezone": "America/Los_Angeles",
                      "isp": "Google LLC",
                      "org": "Google Public DNS",
                      "as": "AS15169 Google LLC",
                      "query": "8.8.8.8"
                    }
                    """),
                apiUrl: ApiUrlWithoutKeyInQuery);

            var result = await repository.ResolveIpToLocationAsync("8.8.8.8");

            result.Should().NotBeNull();
            result!.CountryCode.Should().Be("US");
            result.CountryName.Should().Be("United States",
                because: "ip-api.com puts the country's name in 'country', where ipapi.co puts its "
                    + "code - reading them in the wrong order files a code as a name");
            result.Region.Should().Be("California",
                because: "'regionName' is the name and 'region' is the code at this provider only, "
                    + "so consulting the unambiguous name first is what keeps 'CA' out of here");
            result.LocationCode.Should().Be("CA",
                because: "the code still has to land somewhere, and for this provider the bare "
                    + "'region' is the only place it is");
            result.City.Should().Be("Mountain View");
            result.Latitude.Should().BeApproximately(37.4056, 0.0001,
                because: "this provider abbreviates the coordinates to lat/lon");
            result.IspName.Should().Be("Google LLC");
        }

        /// <summary>
        /// ipapi.co, from a live response captured from its keyless endpoint.
        /// </summary>
        /// <remarks>
        /// Two traps: <c>country</c> here is the ISO code, not the name, and <c>asn</c> is the
        /// identifier "AS15169" rather than an operator name.
        /// </remarks>
        [Fact]
        public async Task An_ipapi_co_payload_does_not_mistake_its_country_code_for_a_country_name()
        {
            var repository = CreateRepository(
                _ => Json(
                    """
                    {
                      "ip": "8.8.8.8",
                      "city": "Mountain View",
                      "region": "California",
                      "region_code": "CA",
                      "country": "US",
                      "country_name": "United States",
                      "country_code": "US",
                      "continent_code": "NA",
                      "latitude": 37.42301,
                      "longitude": -122.083352,
                      "asn": "AS15169",
                      "org": "Google LLC"
                    }
                    """),
                apiUrl: ApiUrlWithoutKeyInQuery);

            var result = await repository.ResolveIpToLocationAsync("8.8.8.8");

            result.Should().NotBeNull();
            result!.CountryCode.Should().Be("US");
            result.CountryName.Should().Be("United States",
                because: "'country' is the ISO code at this provider, so 'country_name' has to win "
                    + "or the response reports the country as 'US'");
            result.Region.Should().Be("California");
            result.LocationCode.Should().Be("CA");
            result.IspName.Should().Be("Google LLC",
                because: "'asn' here is the identifier AS15169, not an operator name, so it must "
                    + "never be read as one - 'org' is the name this provider gives");
            result.Longitude.Should().BeApproximately(-122.083352, 0.0001);
        }

        /// <summary>
        /// ipgeolocation.io, from its documented response shape.
        /// </summary>
        /// <remarks>
        /// This provider nests its geography under <c>location</c> and quotes its coordinates as
        /// strings — the shape that used to fail the entire payload on a type mismatch.
        /// </remarks>
        [Fact]
        public async Task An_ipgeolocation_io_payload_is_read_through_its_nesting_and_quoted_numbers()
        {
            var repository = CreateRepository(
                _ => Json(
                    """
                    {
                      "ip": "8.8.8.8",
                      "location": {
                        "country_code2": "US",
                        "country_name": "United States",
                        "state_prov": "California",
                        "state_code": "US-CA",
                        "city": "Mountain View",
                        "latitude": "37.42240",
                        "longitude": "-122.08421",
                        "continent_code": "NA",
                        "continent_name": "North America",
                        "country_flag": "https://ipgeolocation.io/static/flags/us_64.png"
                      },
                      "asn": {
                        "as_number": "AS15169",
                        "organization": "Google LLC"
                      }
                    }
                    """),
                apiUrl: ApiUrlWithKeyInQuery);

            var result = await repository.ResolveIpToLocationAsync("8.8.8.8");

            result.Should().NotBeNull();
            result!.CountryCode.Should().Be("US",
                because: "this provider nests geography under 'location' and names the code "
                    + "'country_code2', so a flat read of 'country_code' finds nothing");
            result.CountryName.Should().Be("United States");
            result.Region.Should().Be("California");
            result.City.Should().Be("Mountain View");
            result.ContinentName.Should().Be("North America");
            result.Latitude.Should().BeApproximately(37.4224, 0.0001,
                because: "the coordinates arrive quoted; a bound DTO throws on the type mismatch "
                    + "and used to take the country and city down with it");
            result.Longitude.Should().BeApproximately(-122.08421, 0.0001);
            result.CountryFlagPngUrl.Should().Be("https://ipgeolocation.io/static/flags/us_64.png");
            result.IspName.Should().Be("Google LLC",
                because: "the operator name is on the asn object, not at the root");
        }

        [Fact]
        public async Task An_ipv6_address_gets_a_numeric_form_of_its_own()
        {
            var repository = CreateRepository(
                _ => Json("""{"country_code":"CH"}"""),
                apiUrl: ApiUrlWithKeyInQuery);

            var first = await repository.ResolveIpToLocationAsync("2001:4860:4860::8888");
            var second = await repository.ResolveIpToLocationAsync("2606:4700:4700::1111");

            first!.StartIpNumber.Should().NotBe(0,
                because: "every IPv6 address collapsing to 0 makes them all compare equal, so an "
                    + "exact-match query on this field matches every IPv6 address ever recorded");
            second!.StartIpNumber.Should().NotBe(first.StartIpNumber,
                because: "two different addresses that share a number cannot be told apart by the "
                    + "range searches this field exists to serve");
            second.StartIpNumber.Should().BeGreaterThan(first.StartIpNumber,
                because: "2606:: sorts above 2001::, and ordering is the property that survives "
                    + "even though 128 bits cannot be held exactly in a double");
        }

        [Fact]
        public async Task An_ipv4_address_keeps_the_numeric_form_it_has_always_had()
        {
            var repository = CreateRepository(
                _ => Json("""{"country_code":"US"}"""),
                apiUrl: ApiUrlWithKeyInQuery);

            var result = await repository.ResolveIpToLocationAsync("8.8.8.8");

            result!.StartIpNumber.Should().Be(134744072,
                because: "8*2^24 + 8*2^16 + 8*2^8 + 8 is what was stored for this address before, "
                    + "and a stored number that shifts meaning breaks every range already recorded");
            result.LastIpNumber.Should().Be(134744072);
        }

        [Fact]
        public async Task An_ipv4_mapped_ipv6_address_numbers_the_same_as_its_ipv4_form()
        {
            var repository = CreateRepository(
                _ => Json("""{"country_code":"US"}"""),
                apiUrl: ApiUrlWithKeyInQuery);

            var mapped = await repository.ResolveIpToLocationAsync("::ffff:8.8.8.8");

            mapped!.StartIpNumber.Should().Be(134744072,
                because: "it is the same host written another way, and giving it a second number "
                    + "would hide one record from a range search that found the other");
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
            string? cacheSeconds = "300",
            string? secretId = null)
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
                    ["GeolocationApiKeySecretId"] = secretId,
                    ["GeolocationCacheSeconds"] = cacheSeconds,

                    // The gate is what is under test, not the wait. One millisecond exercises the
                    // same code path as the configured second.
                    ["GeolocationProviderDelayMilliseconds"] = "1"
                })
                .Build();

            // A real container rather than a mocked IServiceScopeFactory: the repository is a
            // singleton and reaches the scoped ISecretService by opening a scope, which is the part
            // worth exercising. A mock would let a wiring mistake through.
            var services = new ServiceCollection();

            services.AddScoped(_ => _secretService.Object);

            return new GeolocationRepository(
                _cacheClient.Object,
                httpClientFactory.Object,
                configuration,
                _vault.Object,
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                NullLogger<GeolocationRepository>.Instance);
        }
    }
}
