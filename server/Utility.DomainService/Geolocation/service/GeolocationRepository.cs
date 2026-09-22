using System.Globalization;
using System.Net;
using System.Text.Json;
using Blocks.Genesis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Utility.DomainService.Geolocation.service
{
    /// <summary>
    /// IP geolocation against a single configured provider, with the provider's API key held in
    /// the cloud vault.
    /// </summary>
    /// <remarks>
    /// Three decisions here are load-bearing, and all three exist because the provider is a
    /// metered third party rather than something this service owns:
    ///
    /// <para>
    /// <b>The key comes from the vault, not from configuration.</b> <see cref="IVault"/> is the
    /// same secret store the payment key ring uses, so rotating the provider key is an operations
    /// task rather than a redeploy. A committed <c>GeolocationApiKey</c> is still read as a
    /// fallback so a developer without vault access can work, but the vault wins whenever it
    /// answers.
    /// </para>
    ///
    /// <para>
    /// <b>Calls are serialized behind a gate with a deliberate delay.</b> The provider tiers this
    /// service runs on are billed and rate limited per second, and a burst of lookups that all
    /// come back 429 is worse than the same lookups taking longer: by the time a 429 reaches the
    /// caller it is indistinguishable from a bad address. The gate is process-wide on purpose -
    /// the rate limit belongs to the API key, which is shared, not to a request or a tenant.
    /// </para>
    ///
    /// <para>
    /// <b>Only successful lookups are cached, and never a placeholder.</b> Caching a failure means
    /// one bad minute at the provider is served back for the whole TTL, and caching a synthesized
    /// "Unknown" record makes that failure permanent and invisible.
    /// </para>
    /// </remarks>
    public sealed class GeolocationRepository : IGeolocationRepository
    {
        private const string ApiUrlKey = "GeolocationApiUrl";
        private const string ApiKeyConfigurationKey = "GeolocationApiKey";
        private const string ApiKeySecretNameKey = "GeolocationApiKeySecretName";
        private const string CacheSecondsKey = "GeolocationCacheSeconds";
        private const string ProviderDelayKey = "GeolocationProviderDelayMilliseconds";

        // Short enough that a provider correction or a re-homed address is picked up within the
        // window an operator would tolerate, long enough that a page showing the same visitor
        // repeatedly costs one provider call rather than one per render.
        private const long DefaultCacheSeconds = 300;
        private const int DefaultProviderDelayMilliseconds = 1000;

        private readonly ICacheClient _cacheClient;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IVault _vault;
        private readonly ILogger<GeolocationRepository> _logger;

        private readonly string? _apiUrl;
        private readonly string _apiKeySecretName;
        private readonly string? _configuredApiKey;
        private readonly long _cacheSeconds;
        private readonly int _providerDelayMilliseconds;

        // One in flight at a time. See the remarks on the class: the limit being protected is the
        // API key's, so the gate has to outlive any single request - this type is a singleton.
        private readonly SemaphoreSlim _providerGate = new(1, 1);

        // Resolved on first use and then reused. Startup does not know whether geolocation will
        // ever be asked for, and a vault round trip per lookup would dominate the lookup itself.
        // Written only while holding _providerGate.
        private string? _resolvedApiKey;
        private bool _apiKeyResolved;

        public GeolocationRepository(
            ICacheClient cacheClient,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IVault vault,
            ILogger<GeolocationRepository> logger)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            _cacheClient = cacheClient;
            _httpClientFactory = httpClientFactory;
            _vault = vault;
            _logger = logger;

            _apiUrl = configuration[ApiUrlKey];
            _configuredApiKey = configuration[ApiKeyConfigurationKey];
            _apiKeySecretName = string.IsNullOrWhiteSpace(configuration[ApiKeySecretNameKey])
                ? ApiKeyConfigurationKey
                : configuration[ApiKeySecretNameKey]!;
            _cacheSeconds = ReadPositiveLong(
                configuration,
                CacheSecondsKey,
                DefaultCacheSeconds);
            _providerDelayMilliseconds = (int)ReadPositiveLong(
                configuration,
                ProviderDelayKey,
                DefaultProviderDelayMilliseconds);
        }

        public async Task<IpLookup?> ResolveIpToLocationAsync(
            string ipAddress,
            CancellationToken cancellationToken = default)
        {
            // Validated before anything else: the address is caller-supplied and is about to be
            // interpolated into a URL, so a value that is not an address never reaches the
            // provider, the cache key, or the log.
            if (!IPAddress.TryParse(ipAddress, out _))
            {
                _logger.LogInformation(
                    "Geolocation lookup skipped Reason=address_not_parseable");

                return null;
            }

            var cacheKey = CacheKey(ipAddress);

            var cached = await TryReadCacheAsync(cacheKey);

            if (cached != null)
            {
                return cached;
            }

            if (string.IsNullOrWhiteSpace(_apiUrl))
            {
                _logger.LogWarning(
                    "Geolocation lookup unavailable Reason=provider_url_not_configured Key={Key}",
                    ApiUrlKey);

                return null;
            }

            await _providerGate.WaitAsync(cancellationToken);

            try
            {
                // Re-read inside the gate. A bulk request that repeats an address, or two requests
                // for the same visitor, would otherwise each pay the delay and a provider call for
                // an answer the first one already cached.
                cached = await TryReadCacheAsync(cacheKey);

                if (cached != null)
                {
                    return cached;
                }

                await Task.Delay(_providerDelayMilliseconds, cancellationToken);

                var apiKey = await ResolveApiKeyAsync(cancellationToken);
                var lookup = await FetchFromProviderAsync(ipAddress, apiKey, cancellationToken);

                if (lookup != null)
                {
                    await TryWriteCacheAsync(cacheKey, lookup);
                }

                return lookup;
            }
            finally
            {
                _providerGate.Release();
            }
        }

        public async Task<IpLookup[]> ResolveMultipleIpsToCountryAsync(
            IEnumerable<string> ipAddresses,
            CancellationToken cancellationToken = default)
        {
            if (ipAddresses == null)
            {
                return [];
            }

            var results = new List<IpLookup>();

            // Sequential, not Task.WhenAll: the gate serializes these anyway, so running them in
            // parallel buys nothing and loses the input ordering.
            foreach (var ipAddress in ipAddresses)
            {
                var lookup = await ResolveIpToLocationAsync(ipAddress, cancellationToken);

                if (lookup != null)
                {
                    results.Add(lookup);
                }
            }

            return [.. results];
        }

        /// <summary>
        /// Vault first, committed configuration second. Returns null when neither holds a key,
        /// which is a valid state: some providers (ip-api.com) are keyless.
        /// </summary>
        private async Task<string?> ResolveApiKeyAsync(CancellationToken cancellationToken)
        {
            if (_apiKeyResolved)
            {
                return _resolvedApiKey;
            }

            try
            {
                var secrets = await _vault.ProcessSecretsAsync([_apiKeySecretName]);

                if (secrets != null &&
                    secrets.TryGetValue(_apiKeySecretName, out var secret) &&
                    !string.IsNullOrWhiteSpace(secret))
                {
                    _resolvedApiKey = secret;
                    _apiKeyResolved = true;

                    _logger.LogInformation(
                        "Geolocation provider key resolved Source=vault Secret={Secret}",
                        _apiKeySecretName);

                    return _resolvedApiKey;
                }

                _logger.LogWarning(
                    "Geolocation provider key not in the vault Secret={Secret} Fallback=configuration",
                    _apiKeySecretName);
            }
            catch (Exception exception)
            {
                // A vault that cannot be reached must not take geolocation down while a usable key
                // sits in configuration; it is logged loudly instead.
                _logger.LogError(
                    exception,
                    "Reading the geolocation provider key from the vault failed Secret={Secret} Fallback=configuration",
                    _apiKeySecretName);
            }

            cancellationToken.ThrowIfCancellationRequested();

            _resolvedApiKey = string.IsNullOrWhiteSpace(_configuredApiKey)
                ? null
                : _configuredApiKey;
            _apiKeyResolved = true;

            return _resolvedApiKey;
        }

        /// <summary>
        /// Calls the provider named by <c>GeolocationApiUrl</c>. The URL carries an <c>{ip}</c>
        /// placeholder and, for providers that authenticate in the query string, an
        /// <c>{apiKey}</c> one; without <c>{apiKey}</c> the key is sent as an <c>X-API-Key</c>
        /// header instead.
        /// </summary>
        private async Task<IpLookup?> FetchFromProviderAsync(
            string ipAddress,
            string? apiKey,
            CancellationToken cancellationToken)
        {
            var requestUrl = _apiUrl!.Replace(
                "{ip}",
                Uri.EscapeDataString(ipAddress),
                StringComparison.Ordinal);

            var keyBelongsInUrl = requestUrl.Contains("{apiKey}", StringComparison.Ordinal);

            if (keyBelongsInUrl)
            {
                requestUrl = requestUrl.Replace(
                    "{apiKey}",
                    Uri.EscapeDataString(apiKey ?? string.Empty),
                    StringComparison.Ordinal);
            }

            try
            {
                // A per-call request message rather than DefaultRequestHeaders: this type is a
                // singleton, and mutating shared default headers to carry a per-call secret is a
                // race waiting for the day the gate is widened.
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

                if (!keyBelongsInUrl && !string.IsNullOrWhiteSpace(apiKey))
                {
                    request.Headers.Add("X-API-Key", apiKey);
                }

                var httpClient = _httpClientFactory.CreateClient();

                using var response = await httpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Geolocation provider call failed Status={Status}",
                        (int)response.StatusCode);

                    return null;
                }

                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                var payload = ProviderPayload.Parse(content);

                return payload == null
                    ? null
                    : Map(ipAddress, payload);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller gave up. That is not a provider failure and must not be reported as
                // one, nor cached.
                throw;
            }
            catch (Exception exception)
            {
                // Deliberately broad. Every way a third-party HTTP call can fail - transport,
                // timeout, a payload that does not deserialize, a provider that changed shape -
                // has the same correct answer here: no location. Letting it escape would turn a
                // provider's bad afternoon into 500s on an endpoint whose contract already has a
                // way to say "could not locate".
                _logger.LogWarning(
                    exception,
                    "Geolocation provider call could not be completed");

                return null;
            }
        }

        /// <summary>
        /// Maps the provider payload. Each field lists every spelling the supported providers use
        /// for it, unambiguous names first.
        /// </summary>
        /// <remarks>
        /// The order within a field is the whole design, because the providers do not merely spell
        /// the same field differently - they disagree on what a name <i>means</i>:
        /// <list type="bullet">
        /// <item><c>country</c> is the country's name at ip-api.com and its ISO code at ipapi.co.</item>
        /// <item><c>region</c> is the subdivision's name at abstractapi and ipapi.co, and its ISO
        /// code at ip-api.com, which puts the name in <c>regionName</c>.</item>
        /// </list>
        /// A name that means one thing everywhere is therefore always consulted before an ambiguous
        /// one, so the ambiguous name is only ever reached for the provider that has no
        /// alternative. Getting this backwards does not fail - it silently files a region code as a
        /// region name.
        /// <para>
        /// ipgeolocation.io nests its geography under <c>location</c> and its operator under
        /// <c>asn</c>, which is why the same fields appear again with a dotted prefix.
        /// </para>
        /// </remarks>
        private static IpLookup Map(
            string ipAddress,
            ProviderPayload payload)
        {
            var ipNumber = ConvertIpToNumber(ipAddress);

            // ipgeolocation.io reports the country code under location.country_code2; the others
            // agree on country_code / countryCode, which normalize to the same key.
            var countryCode = payload.Text("countrycode", "location.countrycode2");

            // Last resort is the bare "region", which only ip-api.com uses for the code - everyone
            // else who has a code gives it an unambiguous name, and is matched before this.
            var regionIsoCode = payload.Text(
                "regionisocode", "regioncode", "location.statecode", "region");

            return new IpLookup
            {
                StartIp = ipAddress,
                LastIp = ipAddress,
                StartIpNumber = ipNumber,
                LastIpNumber = ipNumber,
                LocationCode = Or(regionIsoCode, countryCode),
                LocationCodeAsRegistered = Or(regionIsoCode, countryCode),
                ContinentCode = payload.Text("continentcode", "location.continentcode"),
                CountryCode = countryCode,
                ContinentName = payload.Text(
                    "continentname", "location.continentname", "continent"),
                CountryName = payload.Text("countryname", "location.countryname", "country"),
                City = payload.Text("city", "location.city"),
                Region = payload.Text(
                    "regionname", "stateprov", "location.stateprov", "region"),
                Latitude = payload.Number("latitude", "location.latitude", "lat"),
                Longitude = payload.Number("longitude", "location.longitude", "lon", "lng"),
                CountryFlagSvgUrl = payload.Text("countryflagsvgurl", "flag.svg"),
                CountryFlagPngUrl = payload.Text(
                    "countryflagpngurl", "flag.png", "location.countryflag", "countryflag"),

                // Never the bare "asn": ipapi.co sends it as "AS15169", an identifier rather than
                // an operator name. ipgeolocation.io's asn object is the one that carries the name.
                IspName = payload.Text(
                    "ispname", "isp", "connection.ispname", "asn.organization", "company.name", "org")
            };
        }

        private static string Or(string first, string second) =>
            string.IsNullOrEmpty(first) ? second : first;

        private static string CacheKey(string ipAddress) =>
            $"ip_lookup_{ipAddress}";

        private async Task<IpLookup?> TryReadCacheAsync(string cacheKey)
        {
            try
            {
                var cached = await _cacheClient.GetStringValueAsync(cacheKey);

                return string.IsNullOrEmpty(cached)
                    ? null
                    : JsonSerializer.Deserialize<IpLookup>(cached);
            }
            catch (Exception exception)
            {
                // An unreachable or poisoned cache degrades to a provider call; it must not fail
                // the lookup.
                _logger.LogWarning(
                    exception,
                    "Reading the geolocation cache failed");

                return null;
            }
        }

        private async Task TryWriteCacheAsync(
            string cacheKey,
            IpLookup lookup)
        {
            try
            {
                await _cacheClient.AddStringValueAsync(
                    cacheKey,
                    JsonSerializer.Serialize(lookup),
                    _cacheSeconds);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Writing the geolocation cache failed");
            }
        }

        private static long ReadPositiveLong(
            IConfiguration configuration,
            string key,
            long fallback)
        {
            var configured = configuration[key];

            return long.TryParse(
                       configured,
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out var value) &&
                   value > 0
                ? value
                : fallback;
        }

        /// <summary>
        /// IPv4 dotted-quad to its numeric form, for callers that range-search on it. IPv6
        /// addresses have no such form here and resolve to 0.
        /// </summary>
        private static double ConvertIpToNumber(string ipAddress)
        {
            var parts = ipAddress.Split('.');

            if (parts.Length != 4)
            {
                return 0;
            }

            double result = 0;

            for (var index = 0; index < 4; index++)
            {
                if (!int.TryParse(
                        parts[index],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var part))
                {
                    return 0;
                }

                result += part * Math.Pow(256, 3 - index);
            }

            return result;
        }
    }

    /// <summary>
    /// A provider payload, read by field name rather than bound to a type.
    /// </summary>
    /// <remarks>
    /// The same deployment may be pointed at ip-api.com, ipapi.co, ipgeolocation.io or
    /// abstractapi, and they disagree on the spelling of nearly every field: <c>country_code</c>,
    /// <c>countryCode</c> and <c>country_code2</c> all mean the same thing. Keys are therefore
    /// normalized - lowercased, with separators removed - so one lookup name covers every
    /// spelling of it, instead of a DTO carrying a property per provider per field.
    ///
    /// <para>
    /// Reading by name also contains the damage a provider can do by changing shape. A bound DTO
    /// fails the whole payload over one unexpected field - which is how a latitude that arrives
    /// as the string "47.37", as ipgeolocation.io sends it, used to discard the country with it.
    /// Here an unreadable field is empty and the rest of the record survives.
    /// </para>
    /// </remarks>
    internal sealed class ProviderPayload
    {
        private readonly Dictionary<string, JsonElement> _values;

        private ProviderPayload(Dictionary<string, JsonElement> values) =>
            _values = values;

        public static ProviderPayload? Parse(string content)
        {
            using var document = JsonDocument.Parse(content);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

            Flatten(document.RootElement, prefix: null, values);

            return new ProviderPayload(values);
        }

        /// <summary>
        /// The first of <paramref name="candidates"/> that holds a non-empty value, or an empty
        /// string. A nested field is named with a dot: <c>connection.ispname</c>.
        /// </summary>
        public string Text(params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (!_values.TryGetValue(Normalize(candidate), out var value))
                {
                    continue;
                }

                var text = value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False =>
                        value.GetRawText(),
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            return "";
        }

        /// <summary>
        /// The first of <paramref name="candidates"/> that reads as a number, or 0. A provider
        /// that quotes its coordinates is accepted, because several of them do.
        /// </summary>
        public double Number(params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (!_values.TryGetValue(Normalize(candidate), out var value))
                {
                    continue;
                }

                switch (value.ValueKind)
                {
                    case JsonValueKind.Number when value.TryGetDouble(out var number):
                        return number;

                    case JsonValueKind.String
                        when double.TryParse(
                            value.GetString(),
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var parsed):
                        return parsed;

                    default:
                        continue;
                }
            }

            return 0;
        }

        /// <summary>
        /// Flattens nested objects to dotted keys, so <c>{"flag":{"png":"..."}}</c> is reachable
        /// as <c>flag.png</c>. Depth is bounded by the payload, which is size-limited upstream by
        /// the provider.
        /// </summary>
        private static void Flatten(
            JsonElement element,
            string? prefix,
            Dictionary<string, JsonElement> values)
        {
            foreach (var property in element.EnumerateObject())
            {
                var key = prefix == null
                    ? Normalize(property.Name)
                    : $"{prefix}.{Normalize(property.Name)}";

                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    Flatten(property.Value, key, values);
                }
                else
                {
                    // Cloned, because the values outlive the JsonDocument they were read from.
                    // First spelling wins, so a provider that sends both country_code and
                    // countryCode cannot have the second overwrite the first.
                    values.TryAdd(key, property.Value.Clone());
                }
            }
        }

        private static string Normalize(string name)
        {
            var normalized = new char[name.Length];
            var length = 0;

            foreach (var character in name)
            {
                if (character is '_' or '-' or ' ')
                {
                    continue;
                }

                normalized[length++] = char.ToLowerInvariant(character);
            }

            return new string(normalized, 0, length);
        }
    }
}
