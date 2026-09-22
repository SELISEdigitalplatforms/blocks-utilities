# Geolocation Service Configuration

## Overview

The Geolocation Service resolves IP addresses to a location through one configured external
provider. The provider's API key is held in the cloud vault, results are cached for a short
window, and calls are serialized against the provider's rate limit.

Three behaviours are worth knowing before you use it, because they are deliberate:

- **An address that cannot be resolved is absent from the response, not present and empty.** The
  service never synthesizes an "Unknown" location. A response with no lookups reports
  `isSuccess: false`.
- **Lookups are throttled.** Each cache miss waits out a configured delay and holds a
  process-wide gate, so ten uncached addresses take about ten seconds. Cached addresses are free.
- **Failures are never cached.** Only a successful lookup is written to the cache.

## Configuration

| Key | Required | Default | Purpose |
| --- | --- | --- | --- |
| `GeolocationApiUrl` | yes | — | Provider URL template. Without it, every lookup reports that it could not locate. |
| `GeolocationApiKeySecretName` | no | `GeolocationApiKey` | Name of the vault secret holding the provider key. |
| `GeolocationApiKey` | no | — | Fallback provider key, read only when the vault has none. |
| `GeolocationCacheSeconds` | no | `300` | Cache TTL for a successful lookup. Unset, zero, negative or unparseable falls back to the default. |
| `GeolocationProviderDelayMilliseconds` | no | `1000` | Delay paid before each provider call. Match it to the provider's rate limit. |

### Where the provider key comes from

The key is resolved once per process, on the first lookup, in this order:

1. The cloud vault (Genesis `IVault`), under `GeolocationApiKeySecretName`. This is the source to
   use — rotating the key is an operations task and needs no redeploy.
2. The `GeolocationApiKey` configuration value, if the vault holds nothing or cannot be reached.
   This exists so a developer without vault access can run the service; it is not the deployment
   path.

A provider that needs no key at all (ip-api.com) is a valid configuration: leave both unset and no
key is sent.

### Provider URL templates

```json
{
  "GeolocationApiUrl": "<your-api-url>"
}
```

- `{ip}` — replaced with the target IP address (URL-encoded).
- `{apiKey}` — optional. If present, the key is inserted there; if absent, the key is sent as an
  `X-API-Key` request header instead.

#### Example 1: ip-api.com (free, no API key required)
```json
{ "GeolocationApiUrl": "http://ip-api.com/json/{ip}" }
```

#### Example 2: ipapi.co (header-based authentication)
```json
{ "GeolocationApiUrl": "https://ipapi.co/{ip}/json/" }
```

#### Example 3: ipgeolocation.io (URL-based authentication)
```json
{ "GeolocationApiUrl": "https://api.ipgeolocation.io/ipgeo?ip={ip}&apiKey={apiKey}" }
```

#### Example 4: abstractapi.com (URL-based authentication)
```json
{ "GeolocationApiUrl": "https://ipgeolocation.abstractapi.com/v1/?api_key={apiKey}&ip_address={ip}" }
```

## API Endpoints

### 1. LocateIp - Locate Specific IP Addresses

**Endpoint:** `GET /Geolocation/LocateIp`

**Description:** Retrieves geolocation information for specified IP addresses.

**Parameters:**
- `IpAddresses` (query, array of strings): Collection of IP addresses to locate (max 10)
- `ProjectKey` (query, string, optional): Project/tenant identifier

**Example Request:**
```
GET /Geolocation/LocateIp?IpAddresses=8.8.8.8&IpAddresses=1.1.1.1
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
      "locationCode": "CA",
      "locationCodeAsRegistered": "CA",
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

Addresses that could not be resolved are omitted from `ipLookups`, so it may be shorter than
`IpAddresses`. If none resolved, the response is:

```json
{
  "isSuccess": false,
  "ipLookups": null,
  "errorMessage": "IP address could not be located"
}
```

### 2. Locate - Locate Current Request IP

**Endpoint:** `GET /Geolocation/Locate`

**Description:** Automatically extracts and locates IP addresses from the current HTTP request
context.

**Parameters:**
- `ProjectKey` (query, string, optional): Project/tenant identifier

**Example Request:**
```
GET /Geolocation/Locate
```

**Response:** Same structure as LocateIp endpoint

**IP Address Extraction:**
The endpoint extracts IP addresses from:
- `X-Forwarded-For` header (for requests through proxies/load balancers)
- Direct connection remote IP address

Only the first 10 addresses of a forwarded chain are looked up. The header is client-supplied, so
the addresses it names are not evidence of where the caller actually is — anything that has to be
trusted should come from the connection.

## Features

### Caching
- Successful lookups are cached for `GeolocationCacheSeconds` (default 300).
- Cache key format: `ip_lookup_{ipAddress}`.
- Failures and unresolvable addresses are never cached.
- An unreachable cache degrades to a provider call; it does not fail the lookup.

### Rate limiting
- One provider call at a time, process-wide, because the limit belongs to the shared API key.
- Each call waits `GeolocationProviderDelayMilliseconds` first.
- The cache is re-checked inside the gate, so a repeated address in one bulk request costs one
  provider call rather than one per entry.

### Multiple IP support
- Up to 10 IP addresses per request.
- Processed sequentially, in the order asked, because the gate serializes them anyway.

### Input validation
- An entry that does not parse as an IP address is rejected before any provider call.
- Addresses are URL-encoded into the provider URL.

### Flexible provider response mapping
Payloads are read by field name with the separators removed and the case folded, so
`country_code`, `countryCode` and `Country_Code` are one field. Each value names every spelling the
supported providers use, most specific first:

| Field | Provider spellings, in the order consulted |
| --- | --- |
| Country code | `country_code`, `countryCode`, `location.country_code2` |
| Country name | `country_name`, `location.country_name`, `country` |
| Continent | `continent_code`, `location.continent_code`; `continent_name`, `location.continent_name`, `continent` |
| Region | `regionName`, `state_prov`, `location.state_prov`, `region` |
| Region ISO code | `region_iso_code`, `region_code`, `location.state_code`, `region` |
| Coordinates | `latitude`/`longitude`, `location.latitude`/`location.longitude`, `lat`/`lon`, `lng` — quoted numbers accepted |
| ISP | `isp_name`, `isp`, `connection.isp_name`, `asn.organization`, `company.name`, `org` |
| Flag | `flag.png`, `flag.svg`, `location.country_flag`, `country_flag` |

A field the provider does not send is empty; it does not discard the rest of the record.

**The order within a row is load-bearing**, because the providers disagree on what a name *means*,
not just on how to spell it:

- `country` is the country's **name** at ip-api.com and its **ISO code** at ipapi.co.
- `region` is the subdivision's **name** at abstractapi and ipapi.co, and its **ISO code** at
  ip-api.com, which puts the name in `regionName`.
- `asn` is an object at ipgeolocation.io (`asn.organization` is the operator) and a bare string
  like `"AS15169"` at ipapi.co, so it is never read as a name.

An unambiguous name is therefore always consulted before an ambiguous one, and the ambiguous name
is only reached for the provider that has no alternative. Reversing a row does not fail — it
silently files a region code as a region name.

ipgeolocation.io nests its geography under `location` and its operator under `asn`, which is why
several fields appear again with a dotted prefix.

Each of the four providers has a test in `XUnitTest/Geolocation/GeolocationRepositoryTests.cs`
built from its real response shape. Add one alongside them when adding a provider.

## Authentication

Both endpoints require authentication based on your application's security configuration.

## Error Handling

- Empty or missing `IpAddresses` → `"IP addresses are required"`.
- More than 10 addresses → `"Maximum 10 IP addresses allowed per request"`.
- No addresses in the request context (`Locate`) → `"No IP addresses found in request context"`.
- Nothing resolved → `"IP address could not be located"`.

Provider failures — a non-2xx response, a transport error, a timeout, a payload that does not
parse — are absorbed by the repository and reported as an address that could not be located. They
are logged with the provider's status code. A cancelled request propagates rather than being
reported as a failed lookup.

## Testing

### Local testing without a vault
Set `GeolocationApiUrl` and, for providers that need one, `GeolocationApiKey` in
`appsettings.Development.json`. The vault is tried first, logs that it found nothing, and the
configured key is used.

`GeolocationApiKey` is deliberately absent from every committed `appsettings*.json`. Add it
locally if you need it, and do not commit the value — the deployed environments read the key from
the vault.

### Against a keyless provider
```json
{ "GeolocationApiUrl": "http://ip-api.com/json/{ip}" }
```

### Faster tests
Set `GeolocationProviderDelayMilliseconds` to `1`. The unit tests do this — the gate is what is
under test, not waiting a real second for it.

## Dependencies

- `IVault` — for the provider API key (Genesis; registered by the host)
- `IHttpClientFactory` — for making HTTP requests to the provider
- `ICacheClient` — for caching successful lookups
- `IConfiguration` — for reading configuration settings
- `ILogger<GeolocationRepository>` — provider and vault failures are logged, not returned
- `Microsoft.AspNetCore.Http` — for HTTP context access

## Performance Considerations

1. **Throughput is bounded by design.** One provider call at a time plus the configured delay. A
   request for ten uncached addresses takes roughly ten times the delay. If that is too slow for a
   caller, the answer is a shorter delay on a paid provider tier, not parallel calls.
2. **Caching.** A short TTL trades provider spend against staleness; raise
   `GeolocationCacheSeconds` before widening the gate.
3. **Timeout.** Consider configuring the HTTP client timeout for the provider.
4. **Rate limiting.** `GeolocationProviderDelayMilliseconds` has to match the provider's limit;
   the default of one second matches the common free tier of one request per second.

## Troubleshooting

### Issue: every lookup reports "IP address could not be located"
- Check that `GeolocationApiUrl` is set; an unconfigured provider logs
  `Reason=provider_url_not_configured`.
- Check the logs for `Geolocation provider call failed Status=...`. A 401 or 403 means the key did
  not arrive: confirm whether the provider expects it in the URL (`{apiKey}` placeholder) or as a
  header.
- A 429 means the delay is shorter than the provider's rate limit.
- Confirm the address parses as an IP address; `Reason=address_not_parseable` is logged when it
  does not.

### Issue: the provider key is not being picked up
- The logs say which source won: `Geolocation provider key resolved Source=vault`, or a warning
  that it was not in the vault followed by the configuration fallback.
- Check `GeolocationApiKeySecretName` matches the secret's name in the vault.
- A vault error is logged as `Reading the geolocation provider key from the vault failed`; the
  service falls back to configuration rather than failing.
- The key is resolved once per process. Rotating it in the vault needs a restart to be picked up.

### Issue: bulk lookups are slow
This is the intended behaviour — see **Rate limiting**. Lower
`GeolocationProviderDelayMilliseconds` if the provider's tier allows it, send fewer addresses, or
raise `GeolocationCacheSeconds` so repeat addresses stay cached.

### Issue: a field is empty in the response
The provider does not send it, or sends it under a spelling not listed in **Flexible provider
response mapping**. Add the spelling to the candidate list in `GeolocationRepository.Map`.
