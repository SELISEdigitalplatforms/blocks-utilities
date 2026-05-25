# Geolocation Service Configuration

## Overview

The Geolocation Service provides IP geolocation lookup functionality with support for external API providers. The service can fetch geolocation data from external APIs configured via `appsettings.json`.

## Configuration

Configure the geolocation API settings in your `appsettings.json`:

### Configuration Settings

```json
{
  "GeolocationApiUrl": "<your-api-url>",
  "GeolocationApiKey": "<your-api-key>"
}
```

### Configuration Examples

#### Example 1: ip-api.com (Free, No API Key Required)
```json
{
  "GeolocationApiUrl": "http://ip-api.com/json/{ip}",
  "GeolocationApiKey": ""
}
```

#### Example 2: ipapi.com (Header-based authentication)
```json
{
  "GeolocationApiUrl": "https://ipapi.co/{ip}/json/",
  "GeolocationApiKey": "your-api-key-here"
}
```

#### Example 3: ipgeolocation.io (URL-based authentication)
```json
{
  "GeolocationApiUrl": "https://api.ipgeolocation.io/ipgeo?ip={ip}&apiKey={apiKey}",
  "GeolocationApiKey": "your-api-key-here"
}
```

#### Example 4: abstractapi.com (URL-based authentication)
```json
{
  "GeolocationApiUrl": "https://ipgeolocation.abstractapi.com/v1/?api_key={apiKey}&ip_address={ip}",
  "GeolocationApiKey": "your-api-key-here"
}
```

**Note:** 
- Replace `{ip}` placeholder - automatically replaced with the target IP address
- Replace `{apiKey}` placeholder (optional) - if present in URL, the API key will be inserted there; otherwise, it will be added to the request header as `X-API-Key`

## API Endpoints

### 1. LocateIp - Locate Specific IP Addresses

**Endpoint:** `GET /Geolocation/LocateIp`

**Description:** Retrieves geolocation information for specified IP addresses.

**Parameters:**
- `IpAddresses` (query, array of strings): Collection of IP addresses to locate (max 10)
- `UseCustomProvider` (query, boolean): Whether to use the external API provider (default: false)
- `ProjectKey` (query, string, optional): Project/tenant identifier

**Example Request:**
```
GET /Geolocation/LocateIp?IpAddresses=8.8.8.8&IpAddresses=1.1.1.1&UseCustomProvider=true
```

**Example Response:**
```json
{
  "isSuccess": true,
  "ipLookups": [
    {
      "startIp": "8.8.8.8",
      "lastIp": "8.8.8.8",
      "startIpNumber": 134744072,
      "lastIpNumber": 134744072,
      "locationCode": "US",
      "locationCodeAsRegistered": "US",
      "continentCode": "NA",
      "countryCode": "US",
      "continentName": "North America",
      "countryName": "United States",
      "city": "Mountain View",
      "region": "California",
      "latitude": 37.386,
      "longitude": -122.0838,
      "countryFlagSvgUrl": "",
      "countryFlagPngUrl": "",
      "ispName": "Google LLC"
    }
  ],
  "errorMessage": null
}
```

### 2. Locate - Locate Current Request IP

**Endpoint:** `GET /Geolocation/Locate`

**Description:** Automatically extracts and locates IP addresses from the current HTTP request context.

**Parameters:**
- `UseCustomProvider` (query, boolean): Whether to use the external API provider (default: false)
- `ProjectKey` (query, string, optional): Project/tenant identifier

**Example Request:**
```
GET /Geolocation/Locate?UseCustomProvider=true
```

**Response:** Same structure as LocateIp endpoint

**IP Address Extraction:**
The endpoint extracts IP addresses from:
- `X-Forwarded-For` header (for requests through proxies/load balancers)
- Direct connection remote IP address

## Features

### Caching
- IP lookup results are cached for 1 hour to reduce external API calls
- Cache key format: `ip_lookup_{ipAddress}`

### Fallback Mechanism
- If external API is not configured, returns placeholder data
- If external API call fails, automatically falls back to placeholder data
- Ensures service availability even when external API is unavailable

### Multiple IP Support
- Supports bulk lookup of up to 10 IP addresses per request
- Processes IP lookups concurrently for better performance

### Flexible API Response Mapping
The service supports various geolocation API response formats with alternative property names:
- CountryCode / Country
- ContinentCode / Continent
- Region / RegionName
- Latitude / Lat
- Longitude / Lon
- IspName / Isp / Org

## Authentication

Both endpoints require authentication based on your application's security configuration.

## Error Handling

The service includes comprehensive error handling:
- Invalid or empty IP addresses
- Maximum IP limit validation (10 per request)
- External API failures
- Network timeouts
- JSON deserialization errors

All errors are gracefully handled and return appropriate error messages in the response.

## Testing

### Local Testing Without External API
If no configuration is set in `appsettings.json`, the service will use placeholder data:
```json
{
  "countryCode": "US",
  "countryName": "United States",
  "city": "Unknown",
  "region": "Unknown"
}
```

### Testing With External API
1. Add configuration to `appsettings.json`:
```json
{
  "GeolocationApiUrl": "http://ip-api.com/json/{ip}",
  "GeolocationApiKey": ""
}
```
2. Make a request with `UseCustomProvider=true`
3. Verify real geolocation data is returned

## Dependencies

- `IHttpClientFactory` - For making HTTP requests to external APIs
- `ICacheClient` - For caching IP lookup results
- `IConfiguration` - For reading configuration settings
- `Microsoft.AspNetCore.Http` - For HTTP context access

## Performance Considerations

1. **Caching:** Results are cached for 1 hour to minimize external API calls
2. **Concurrent Processing:** Multiple IP lookups are processed in parallel
3. **Timeout:** Consider configuring HTTP client timeout for external API calls
4. **Rate Limiting:** Be aware of rate limits on external geolocation APIs

## Troubleshooting

### Issue: Receiving Placeholder Data
**Solution:** 
- Ensure `GeolocationApiUrl` and `GeolocationApiKey` are properly configured in `appsettings.json`
- Set `UseCustomProvider=true` in the request
- Check API key validity
- Verify API URL format includes `{ip}` placeholder

### Issue: API Calls Failing
**Solution:**
- Check network connectivity
- Verify API key is valid and not expired
- Check API provider's rate limits
- Review API provider's documentation for correct URL format
- Verify configuration keys are correctly spelled: `GeolocationApiUrl` and `GeolocationApiKey`

### Issue: Slow Response Times
**Solution:**
- Enable caching (already enabled by default)
- Reduce number of IPs per request
- Consider upgrading to faster API provider plan
- Check network latency to API provider

### Issue: Configuration Not Loading
**Solution:**
- Verify `appsettings.json` is in the correct location
- Check configuration key names: `GeolocationApiUrl` and `GeolocationApiKey`
- Ensure the file is being copied to the output directory
- Restart the application after configuration changes

