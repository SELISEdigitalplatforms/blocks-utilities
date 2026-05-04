# Quick Start Guide - Geolocation API

## 1. Configure appsettings.json (Required)

Add the following to your `appsettings.json`:

```json
{
  "GeolocationApiUrl": "http://ip-api.com/json/{ip}",
  "GeolocationApiKey": ""
}
```

## 2. API Endpoints

### Endpoint 1: Locate Specific IPs
```
GET /Geolocation/LocateIp?IpAddresses=8.8.8.8&UseCustomProvider=true
```

### Endpoint 2: Locate Current Request IP
```
GET /Geolocation/Locate?UseCustomProvider=true
```

## 3. Example Response
```json
{
  "isSuccess": true,
  "ipLookups": [
    {
      "countryCode": "US",
      "countryName": "United States",
      "city": "Mountain View",
      "latitude": 37.386,
      "longitude": -122.0838
    }
  ]
}
```

## 4. Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| IpAddresses | string[] | Yes (LocateIp) | IPs to locate (max 10) |
| UseCustomProvider | boolean | No | Use external API (default: false) |
| ProjectKey | string | No | Tenant identifier |

## 5. Free Provider Recommendation

**ip-api.com** (No API key needed)
- 45 requests/minute
- No registration required
- Perfect for testing

```json
{
  "GeolocationApiUrl": "http://ip-api.com/json/{ip}",
  "GeolocationApiKey": ""
}
```

## 6. Testing

```bash
# Test without external API (uses cache/placeholder)
curl "http://localhost:5000/Geolocation/LocateIp?IpAddresses=8.8.8.8"

# Test with external API
curl "http://localhost:5000/Geolocation/LocateIp?IpAddresses=8.8.8.8&UseCustomProvider=true"

# Test current request IP
curl "http://localhost:5000/Geolocation/Locate?UseCustomProvider=true"
```

## 7. Key Features

✅ **Simple Configuration** - Easy setup via appsettings.json  
✅ **Caching** - Results cached for 1 hour  
✅ **Bulk Lookup** - Up to 10 IPs per request  
✅ **Auto Fallback** - Returns placeholder if API fails  
✅ **Concurrent Processing** - Multiple IPs processed in parallel  
✅ **Flexible Authentication** - Supports both URL and header-based API keys  

## Need More Help?

- **Full Documentation:** See `README.md`
- **Migration Details:** See `../../../GEOLOCATION_MIGRATION_SUMMARY.md`

## Common Issues

**Problem:** Getting placeholder data  
**Solution:** Set `UseCustomProvider=true` and verify configuration in `appsettings.json`

**Problem:** API calls failing  
**Solution:** Check API URL format includes `{ip}` placeholder and verify configuration keys

**Problem:** Configuration not loading  
**Solution:** Verify keys are `GeolocationApiUrl` and `GeolocationApiKey` (case-sensitive), restart application after changes

