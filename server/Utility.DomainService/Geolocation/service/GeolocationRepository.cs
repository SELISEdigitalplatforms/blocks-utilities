using System.Text.Json;
using Blocks.Genesis;
using Microsoft.Extensions.Configuration;
using Utility.DomainService.Geolocation;

namespace Utility.DomainService.Geolocation.service
{
    public class GeolocationRepository : IGeolocationRepository
    {
        private readonly ICacheClient _cacheClient;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly string? _geolocationApiUrl;
        private readonly string? _geolocationApiKey;

        public GeolocationRepository(ICacheClient cacheClient, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _cacheClient = cacheClient;
            _httpClient = httpClientFactory.CreateClient();
            _configuration = configuration;
            _geolocationApiUrl = _configuration["GeolocationApiUrl"];
            _geolocationApiKey = _configuration["GeolocationApiKey"];
        }

        public async Task<bool> IsGeoRestrictionEnabledAsync(string tenantId)
        {
            try
            {
                // Check if geo-restriction is enabled for the tenant
                var cacheKey = $"geo_restriction_enabled_{tenantId}";
                var cachedValue = await _cacheClient.GetStringValueAsync(cacheKey);
                
                if (!string.IsNullOrEmpty(cachedValue))
                {
                    return bool.TryParse(cachedValue, out var result) && result;
                }

                // Default to false if not found
                return false;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> IsCountryBlockedAsync(string countryCode, string tenantId)
        {
            try
            {
                var cacheKey = $"blocked_country_{tenantId}_{countryCode}";
                var cachedValue = await _cacheClient.GetStringValueAsync(cacheKey);
                
                if (!string.IsNullOrEmpty(cachedValue))
                {
                    return bool.TryParse(cachedValue, out var result) && result;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> IsUserBlockedFromCountryAsync(string countryCode, string userId, string tenantId)
        {
            try
            {
                var cacheKey = $"blocked_user_country_{tenantId}_{userId}_{countryCode}";
                var cachedValue = await _cacheClient.GetStringValueAsync(cacheKey);
                
                if (!string.IsNullOrEmpty(cachedValue))
                {
                    return bool.TryParse(cachedValue, out var result) && result;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> IsRoleBlockedFromCountryAsync(string countryCode, IEnumerable<string> roles, string tenantId)
        {
            try
            {
                foreach (var role in roles)
                {
                    var cacheKey = $"blocked_role_country_{tenantId}_{role}_{countryCode}";
                    var cachedValue = await _cacheClient.GetStringValueAsync(cacheKey);
                    
                    if (!string.IsNullOrEmpty(cachedValue) && bool.TryParse(cachedValue, out var result) && result)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public async Task<IpLookup> ResolveIpToCountryAsync(IEnumerable<string> ipAddresses, string tenantId)
        {
            try
            {
                var firstIpAddress = ipAddresses.FirstOrDefault();
                if (string.IsNullOrEmpty(firstIpAddress))
                {
                    return null;
                }

                // Try to get from cache first
                var cacheKey = $"ip_lookup_{firstIpAddress}";
                var cachedValue = await _cacheClient.GetStringValueAsync(cacheKey);
                
                if (!string.IsNullOrEmpty(cachedValue))
                {
                    var cachedLookup = JsonSerializer.Deserialize<IpLookup>(cachedValue);
                    if (cachedLookup != null)
                    {
                        return cachedLookup;
                    }
                }

                // TODO: Implement actual IP geolocation lookup using external service
                // For now, return a placeholder lookup
                var placeholder = CreatePlaceholderLookup(firstIpAddress);
                
                // Cache the result
                var serializedLookup = JsonSerializer.Serialize(placeholder);
                await _cacheClient.AddStringValueAsync(cacheKey, serializedLookup, 3600); // Cache for 1 hour

                return placeholder;
            }
            catch
            {
                return null;
            }
        }

        public async Task<IpLookup[]> ResolveMultipleIpsToCountryAsync(IEnumerable<string> ipAddresses, bool useCustomProvider = false)
        {
            try
            {
                if (!ipAddresses.Any())
                {
                    return Array.Empty<IpLookup>();
                }

                var lookupTasks = ipAddresses.Select(async ip =>
                {
                    // Try to get from cache first
                    var cacheKey = $"ip_lookup_{ip}";
                    var cachedValue = await _cacheClient.GetStringValueAsync(cacheKey);
                    
                    if (!string.IsNullOrEmpty(cachedValue))
                    {
                        var cachedLookup = JsonSerializer.Deserialize<IpLookup>(cachedValue);
                        if (cachedLookup != null)
                        {
                            return cachedLookup;
                        }
                    }

                    // If not in cache, fetch from API
                    var lookup = useCustomProvider 
                        ? await FetchFromExternalApiAsync(ip)
                        : await ResolveIpToCountryAsync(new[] { ip }, "default");
                    
                    // Cache the result if successful
                    if (lookup != null)
                    {
                        var serializedLookup = JsonSerializer.Serialize(lookup);
                        await _cacheClient.AddStringValueAsync(cacheKey, serializedLookup, 3600); // Cache for 1 hour
                    }
                    
                    return lookup;
                });

                var results = await Task.WhenAll(lookupTasks);
                return results.Where(r => r != null).ToArray();
            }
            catch
            {
                return Array.Empty<IpLookup>();
            }
        }

        /// <summary>
        /// Fetches geolocation data from external API configured via configuration.
        /// Supports both URL-based and header-based API key authentication.
        /// </summary>
        private async Task<IpLookup?> FetchFromExternalApiAsync(string ipAddress)
        {
            try
            {
                if (string.IsNullOrEmpty(_geolocationApiUrl))
                {
                    // Fall back to placeholder if API URL is not configured
                    return CreatePlaceholderLookup(ipAddress);
                }

                // Build the API request URL - replace IP placeholder
                var requestUrl = _geolocationApiUrl.Replace("{ip}", ipAddress);
                
                // Check if API key should be in URL or header
                var apiKeyInUrl = requestUrl.Contains("{apiKey}");
                
                if (!string.IsNullOrEmpty(_geolocationApiKey))
                {
                    if (apiKeyInUrl)
                    {
                        // Replace {apiKey} placeholder in URL
                        requestUrl = requestUrl.Replace("{apiKey}", _geolocationApiKey);
                    }
                    else
                    {
                        // Add API key to headers if not in URL
                        _httpClient.DefaultRequestHeaders.Clear();
                        _httpClient.DefaultRequestHeaders.Add("X-API-Key", _geolocationApiKey);
                    }
                }

                var response = await _httpClient.GetAsync(requestUrl);
                
                if (!response.IsSuccessStatusCode)
                {
                    // Fall back to placeholder if API call fails
                    return CreatePlaceholderLookup(ipAddress);
                }

                var content = await response.Content.ReadAsStringAsync();
                var apiResponse = JsonSerializer.Deserialize<GeolocationApiResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (apiResponse == null)
                {
                    return CreatePlaceholderLookup(ipAddress);
                }

                // Map the API response to IpLookup (handle alternative property names)
                var countryCode = apiResponse.CountryCode ?? "";
                var countryName = apiResponse.CountryName ?? apiResponse.Country ?? "";
                var continentCode = apiResponse.ContinentCode ?? "";
                var continentName = apiResponse.ContinentName ?? apiResponse.Continent ?? "";
                var region = apiResponse.Region ?? apiResponse.RegionName ?? "";
                const double epsilon = 1e-6;
                var latitude = Math.Abs(apiResponse.Latitude) > epsilon ? apiResponse.Latitude : apiResponse.Lat;
                var longitude = Math.Abs(apiResponse.Longitude) > epsilon ? apiResponse.Longitude : apiResponse.Lon;
                var ispName = apiResponse.IspName ?? apiResponse.Isp ?? apiResponse.Org ?? "";

                return new IpLookup
                {
                    StartIp = ipAddress,
                    LastIp = ipAddress,
                    StartIpNumber = ConvertIpToNumber(ipAddress),
                    LastIpNumber = ConvertIpToNumber(ipAddress),
                    LocationCode = countryCode,
                    LocationCodeAsRegistered = countryCode,
                    ContinentCode = continentCode,
                    CountryCode = countryCode,
                    ContinentName = continentName,
                    CountryName = countryName,
                    City = apiResponse.City ?? "",
                    Region = region,
                    Latitude = latitude,
                    Longitude = longitude,
                    CountryFlagSvgUrl = apiResponse.CountryFlagSvgUrl ?? "",
                    CountryFlagPngUrl = apiResponse.CountryFlagPngUrl ?? "",
                    IspName = ispName
                };
            }
            catch (Exception)
            {
                // Return placeholder if any error occurs
                return CreatePlaceholderLookup(ipAddress);
            }
        }

        private static IpLookup CreatePlaceholderLookup(string ipAddress)
        {
            // This is a placeholder implementation
            // Used when external API is not configured or fails
            return new IpLookup
            {
                StartIp = ipAddress,
                LastIp = ipAddress,
                StartIpNumber = ConvertIpToNumber(ipAddress),
                LastIpNumber = ConvertIpToNumber(ipAddress),
                LocationCode = "Unknown",
                LocationCodeAsRegistered = "Unknown",
                ContinentCode = "NA",
                CountryCode = "Unknown",
                ContinentName = "Unknown",
                CountryName = "Unknown",
                City = "Unknown",
                Region = "Unknown",
                Latitude = 0.0,
                Longitude = 0.0,
                CountryFlagSvgUrl = "",
                CountryFlagPngUrl = "",
                IspName = "Unknown ISP"
            };
        }

        private static double ConvertIpToNumber(string ipAddress)
        {
            try
            {
                var parts = ipAddress.Split('.');
                if (parts.Length != 4) return 0;

                double result = 0;
                for (int i = 0; i < 4; i++)
                {
                    if (int.TryParse(parts[i], out var part))
                    {
                        result += part * Math.Pow(256, 3 - i);
                    }
                }
                return result;
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// DTO for external geolocation API response.
    /// This class is flexible to work with various geolocation APIs (ip-api.com, ipapi.com, etc.)
    /// </summary>
    internal class GeolocationApiResponse
    {
        public string? CountryCode { get; set; }
        public string? CountryName { get; set; }
        public string? Country { get; set; } // Alternative property name
        public string? ContinentCode { get; set; }
        public string? ContinentName { get; set; }
        public string? Continent { get; set; } // Alternative property name
        public string? City { get; set; }
        public string? Region { get; set; }
        public string? RegionName { get; set; } // Alternative property name
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Lat { get; set; } // Alternative property name
        public double Lon { get; set; } // Alternative property name
        public string? CountryFlagSvgUrl { get; set; }
        public string? CountryFlagPngUrl { get; set; }
        public string? IspName { get; set; }
        public string? Isp { get; set; } // Alternative property name
        public string? Org { get; set; } // Alternative property name
    }
}