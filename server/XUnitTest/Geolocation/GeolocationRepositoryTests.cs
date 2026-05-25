using System.Net;
using System.Text;
using System.Text.Json;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Moq.Protected;
using Utility.DomainService.Geolocation;
using Utility.DomainService.Geolocation.service;

namespace XUnitTest.Geolocation
{
    public class GeolocationRepositoryTests
    {
        private readonly Mock<ICacheClient> _cacheClientMock;

        public GeolocationRepositoryTests()
        {
            _cacheClientMock = new Mock<ICacheClient>();
            _cacheClientMock.Setup(x => x.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync((string?)null);
            _cacheClientMock.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);
        }

        [Fact]
        public async Task IsGeoRestrictionEnabledAsync_ShouldReturnTrue_WhenCachedValueIsTrue()
        {
            _cacheClientMock.Setup(x => x.GetStringValueAsync("geo_restriction_enabled_tenant")).ReturnsAsync("true");
            var repository = CreateRepository();

            var result = await repository.IsGeoRestrictionEnabledAsync("tenant");

            result.Should().BeTrue();
        }

        [Fact]
        public async Task IsGeoRestrictionEnabledAsync_ShouldReturnFalse_WhenCacheThrows()
        {
            _cacheClientMock.Setup(x => x.GetStringValueAsync(It.IsAny<string>())).ThrowsAsync(new Exception("cache error"));
            var repository = CreateRepository();

            var result = await repository.IsGeoRestrictionEnabledAsync("tenant");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task IsGeoRestrictionEnabledAsync_ShouldReturnFalse_WhenCacheIsEmpty()
        {
            _cacheClientMock.Setup(x => x.GetStringValueAsync("geo_restriction_enabled_tenant")).ReturnsAsync((string?)null);
            var repository = CreateRepository();

            var result = await repository.IsGeoRestrictionEnabledAsync("tenant");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task IsCountryBlockedAsync_ShouldReturnFalse_WhenCachedValueIsInvalid()
        {
            _cacheClientMock.Setup(x => x.GetStringValueAsync("blocked_country_tenant_DE")).ReturnsAsync("not-bool");
            var repository = CreateRepository();

            var result = await repository.IsCountryBlockedAsync("DE", "tenant");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task IsCountryBlockedAsync_ShouldReturnFalse_WhenCacheIsEmpty()
        {
            _cacheClientMock.Setup(x => x.GetStringValueAsync("blocked_country_tenant_DE")).ReturnsAsync((string?)null);
            var repository = CreateRepository();

            var result = await repository.IsCountryBlockedAsync("DE", "tenant");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task IsCountryBlockedAsync_ShouldReturnFalse_WhenCacheThrows()
        {
            _cacheClientMock.Setup(x => x.GetStringValueAsync(It.IsAny<string>())).ThrowsAsync(new Exception("cache error"));
            var repository = CreateRepository();

            var result = await repository.IsCountryBlockedAsync("DE", "tenant");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task IsUserBlockedFromCountryAsync_ShouldReturnFalse_WhenCacheIsEmpty()
        {
            var repository = CreateRepository();

            var result = await repository.IsUserBlockedFromCountryAsync("CH", "user-1", "tenant");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task IsUserBlockedFromCountryAsync_ShouldReturnTrue_WhenCachedValueIsTrue()
        {
            _cacheClientMock.Setup(x => x.GetStringValueAsync("blocked_user_country_tenant_user-1_CH")).ReturnsAsync("true");
            var repository = CreateRepository();

            var result = await repository.IsUserBlockedFromCountryAsync("CH", "user-1", "tenant");

            result.Should().BeTrue();
        }

        [Fact]
        public async Task IsUserBlockedFromCountryAsync_ShouldReturnFalse_WhenCacheThrows()
        {
            _cacheClientMock.Setup(x => x.GetStringValueAsync(It.IsAny<string>())).ThrowsAsync(new Exception("cache error"));
            var repository = CreateRepository();

            var result = await repository.IsUserBlockedFromCountryAsync("CH", "user-1", "tenant");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task IsRoleBlockedFromCountryAsync_ShouldReturnTrue_WhenAnyRoleIsBlocked()
        {
            _cacheClientMock.Setup(x => x.GetStringValueAsync("blocked_role_country_tenant_admin_US")).ReturnsAsync("false");
            _cacheClientMock.Setup(x => x.GetStringValueAsync("blocked_role_country_tenant_manager_US")).ReturnsAsync("true");
            var repository = CreateRepository();

            var result = await repository.IsRoleBlockedFromCountryAsync("US", new[] { "admin", "manager" }, "tenant");

            result.Should().BeTrue();
        }

        [Fact]
        public async Task IsRoleBlockedFromCountryAsync_ShouldReturnFalse_WhenNoRoleIsBlocked()
        {
            _cacheClientMock.Setup(x => x.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync("false");
            var repository = CreateRepository();

            var result = await repository.IsRoleBlockedFromCountryAsync("US", new[] { "admin", "manager" }, "tenant");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task IsRoleBlockedFromCountryAsync_ShouldReturnFalse_WhenExceptionOccurs()
        {
            _cacheClientMock.Setup(x => x.GetStringValueAsync(It.IsAny<string>())).ThrowsAsync(new Exception("cache error"));
            var repository = CreateRepository();

            var result = await repository.IsRoleBlockedFromCountryAsync("US", new[] { "admin" }, "tenant");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task ResolveIpToCountryAsync_ShouldReturnNull_WhenInputIsEmpty()
        {
            var repository = CreateRepository();

            var result = await repository.ResolveIpToCountryAsync(Array.Empty<string>(), "tenant");

            result.Should().BeNull();
        }

        [Fact]
        public async Task ResolveIpToCountryAsync_ShouldReturnCachedLookup_WhenCacheContainsValidJson()
        {
            var cached = new IpLookup { StartIp = "8.8.8.8", CountryCode = "US", CountryName = "United States" };
            _cacheClientMock.Setup(x => x.GetStringValueAsync("ip_lookup_8.8.8.8")).ReturnsAsync(JsonSerializer.Serialize(cached));
            var repository = CreateRepository();

            var result = await repository.ResolveIpToCountryAsync(new[] { "8.8.8.8" }, "tenant");

            result.Should().NotBeNull();
            result!.CountryCode.Should().Be("US");
            result.CountryName.Should().Be("United States");
        }

        [Fact]
        public async Task ResolveIpToCountryAsync_ShouldCreatePlaceholder_WhenCacheContainsNullJson()
        {
            _cacheClientMock.Setup(x => x.GetStringValueAsync("ip_lookup_bad-ip")).ReturnsAsync("null");
            var repository = CreateRepository();

            var result = await repository.ResolveIpToCountryAsync(new[] { "bad-ip" }, "tenant");

            result.Should().NotBeNull();
            result!.CountryCode.Should().Be("Unknown");
            result.StartIpNumber.Should().Be(0);
            _cacheClientMock.Verify(x => x.AddStringValueAsync("ip_lookup_bad-ip", It.IsAny<string>(), 3600), Times.Once);
        }

        [Fact]
        public async Task ResolveIpToCountryAsync_ShouldReturnNull_WhenCacheThrows()
        {
            _cacheClientMock.Setup(x => x.GetStringValueAsync(It.IsAny<string>())).ThrowsAsync(new Exception("cache error"));
            var repository = CreateRepository();

            var result = await repository.ResolveIpToCountryAsync(new[] { "1.1.1.1" }, "tenant");

            result.Should().BeNull();
        }

        [Fact]
        public async Task ResolveMultipleIpsToCountryAsync_ShouldReturnEmpty_WhenInputIsEmpty()
        {
            var repository = CreateRepository();

            var result = await repository.ResolveMultipleIpsToCountryAsync(Array.Empty<string>());

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task ResolveMultipleIpsToCountryAsync_ShouldReturnCachedItems_WhenCacheExists()
        {
            _cacheClientMock.Setup(x => x.GetStringValueAsync("ip_lookup_8.8.4.4"))
                .ReturnsAsync(JsonSerializer.Serialize(new IpLookup { StartIp = "8.8.4.4", CountryCode = "US" }));
            var repository = CreateRepository();

            var result = await repository.ResolveMultipleIpsToCountryAsync(new[] { "8.8.4.4" });

            result.Should().HaveCount(1);
            result[0].CountryCode.Should().Be("US");
        }

        [Fact]
        public async Task ResolveMultipleIpsToCountryAsync_ShouldHandleNullCachedLookupAndContinue()
        {
            _cacheClientMock.Setup(x => x.GetStringValueAsync("ip_lookup_4.4.4.4")).ReturnsAsync("null");
            var repository = CreateRepository(apiUrl: null, apiKey: null);

            var result = await repository.ResolveMultipleIpsToCountryAsync(new[] { "4.4.4.4" }, true);

            result.Should().HaveCount(1);
            result[0].CountryCode.Should().Be("Unknown");
        }

        [Fact]
        public async Task ResolveMultipleIpsToCountryAsync_ShouldUsePlaceholder_WhenCustomProviderWithoutApiUrl()
        {
            var repository = CreateRepository(apiUrl: null, apiKey: null);

            var result = await repository.ResolveMultipleIpsToCountryAsync(new[] { "9.9.9.9" }, true);

            result.Should().HaveCount(1);
            result[0].CountryCode.Should().Be("Unknown");
        }

        [Fact]
        public async Task ResolveMultipleIpsToCountryAsync_ShouldMapApiResponse_WhenApiKeyIsInUrl()
        {
            HttpRequestMessage? capturedRequest = null;
            var apiResponse = new
            {
                countryCode = "FR",
                country = "France",
                continent = "Europe",
                city = "Paris",
                regionName = "Ile-de-France",
                lat = 48.85,
                lon = 2.35,
                org = "TestOrg"
            };

            var repository = CreateRepository(
                requestHandler: request =>
                {
                    capturedRequest = request;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(apiResponse), Encoding.UTF8, "application/json")
                    };
                },
                apiUrl: "https://geo.example.com/{ip}?key={apiKey}",
                apiKey: "secret-key");

            var result = await repository.ResolveMultipleIpsToCountryAsync(new[] { "5.5.5.5" }, true);

            result.Should().HaveCount(1);
            result[0].CountryCode.Should().Be("FR");
            result[0].CountryName.Should().Be("France");
            result[0].ContinentName.Should().Be("Europe");
            result[0].Region.Should().Be("Ile-de-France");
            result[0].Latitude.Should().Be(48.85);
            result[0].Longitude.Should().Be(2.35);
            result[0].IspName.Should().Be("TestOrg");
            capturedRequest.Should().NotBeNull();
            capturedRequest!.RequestUri!.ToString().Should().Contain("secret-key");
        }

        [Fact]
        public async Task ResolveMultipleIpsToCountryAsync_ShouldSendApiKeyInHeader_WhenApiKeyNotInUrl()
        {
            HttpRequestMessage? capturedRequest = null;

            var repository = CreateRepository(
                requestHandler: request =>
                {
                    capturedRequest = request;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"countryCode\":\"DE\",\"countryName\":\"Germany\"}")
                    };
                },
                apiUrl: "https://geo.example.com/{ip}",
                apiKey: "header-key");

            var result = await repository.ResolveMultipleIpsToCountryAsync(new[] { "6.6.6.6" }, true);

            result.Should().HaveCount(1);
            result[0].CountryCode.Should().Be("DE");
            capturedRequest.Should().NotBeNull();
            capturedRequest!.Headers.Contains("X-API-Key").Should().BeTrue();
            capturedRequest.Headers.GetValues("X-API-Key").Should().Contain("header-key");
        }

        [Fact]
        public async Task ResolveMultipleIpsToCountryAsync_ShouldFallbackToPlaceholder_WhenApiReturnsNonSuccess()
        {
            var repository = CreateRepository(
                requestHandler: _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
                apiUrl: "https://geo.example.com/{ip}",
                apiKey: "k");

            var result = await repository.ResolveMultipleIpsToCountryAsync(new[] { "7.7.7.7" }, true);

            result.Should().HaveCount(1);
            result[0].CountryCode.Should().Be("Unknown");
        }

        [Fact]
        public async Task ResolveMultipleIpsToCountryAsync_ShouldFallbackToPlaceholder_WhenApiResponseIsNull()
        {
            var repository = CreateRepository(
                requestHandler: _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("null")
                },
                apiUrl: "https://geo.example.com/{ip}",
                apiKey: "k");

            var result = await repository.ResolveMultipleIpsToCountryAsync(new[] { "10.0.0.1" }, true);

            result.Should().HaveCount(1);
            result[0].CountryCode.Should().Be("Unknown");
        }

        [Fact]
        public async Task ResolveMultipleIpsToCountryAsync_ShouldFallbackToPlaceholder_WhenApiThrowsDuringParsing()
        {
            var repository = CreateRepository(
                requestHandler: _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("not-json")
                },
                apiUrl: "https://geo.example.com/{ip}",
                apiKey: "k");

            var result = await repository.ResolveMultipleIpsToCountryAsync(new[] { "10.0.0.2" }, true);

            result.Should().HaveCount(1);
            result[0].CountryCode.Should().Be("Unknown");
        }

        [Fact]
        public async Task ResolveMultipleIpsToCountryAsync_ShouldReturnEmpty_WhenOuterTryCatchHandlesException()
        {
            _cacheClientMock.Setup(x => x.GetStringValueAsync(It.IsAny<string>())).ThrowsAsync(new Exception("cache error"));
            var repository = CreateRepository();

            var result = await repository.ResolveMultipleIpsToCountryAsync(new[] { "1.2.3.4" }, true);

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task ResolveMultipleIpsToCountryAsync_ShouldReturnZeroIpNumber_WhenIpValueIsNull()
        {
            var repository = CreateRepository(apiUrl: null, apiKey: null);

            var result = await repository.ResolveMultipleIpsToCountryAsync(new[] { (string)null! }, true);

            result.Should().HaveCount(1);
            result[0].StartIpNumber.Should().Be(0);
            result[0].LastIpNumber.Should().Be(0);
        }

        private GeolocationRepository CreateRepository(
            Func<HttpRequestMessage, HttpResponseMessage>? requestHandler = null,
            string? apiUrl = null,
            string? apiKey = null)
        {
            var messageHandler = new Mock<HttpMessageHandler>();
            messageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
                {
                    if (requestHandler != null)
                    {
                        return requestHandler(request);
                    }

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{}")
                    };
                });

            var httpClientFactoryMock = new Mock<IHttpClientFactory>();
            httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient(messageHandler.Object));

            var configValues = new Dictionary<string, string?>
            {
                ["GeolocationApiUrl"] = apiUrl,
                ["GeolocationApiKey"] = apiKey
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configValues)
                .Build();

            return new GeolocationRepository(_cacheClientMock.Object, httpClientFactoryMock.Object, configuration);
        }
    }
}
