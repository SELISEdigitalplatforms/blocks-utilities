# Geolocation Service Configuration

## Overview

The Geolocation Service resolves IP addresses to a location through one configured external
provider. The provider's API key is held in a secret store rather than in configuration, results
are cached for a short window, and calls are serialized against the provider's rate limit.

Three behaviours are worth knowing before you use it, because they are deliberate:

- **An address that cannot be resolved is absent from the response, not present and empty.** The
  service never synthesizes an "Unknown" location. If none of them resolve, the response is a
  404 rather than an empty success.
- **Lookups are throttled.** Each cache miss waits out a configured delay and holds a
  process-wide gate, so ten uncached addresses take about ten seconds. Cached addresses are free.
- **Failures are never cached.** Only a successful lookup is written to the cache.

## Configuration

| Key | Required | Default | Purpose |
| --- | --- | --- | --- |
| `GeolocationApiUrl` | yes | — | Provider URL template. Without it, every lookup reports that it could not locate. |
| `GeolocationApiKeySecretId` | no | — | Id of the Blocks Secrets secret holding the provider key. Setting it selects the managed store. |
| `GeolocationApiKeySecretName` | no | `GeolocationApiKey` | Name of the Genesis vault secret, used when no secret id is set. |
| `GeolocationApiKey` | no | — | Fallback provider key, read only when neither store answers. |
| `GeolocationCacheSeconds` | no | `300` | Cache TTL for a successful lookup, and for the resolved provider key. Unset, zero, negative or unparseable falls back to the default. |
| `GeolocationProviderDelayMilliseconds` | no | `1000` | Delay paid before each provider call. Match it to the provider's rate limit. |

### Where the provider key comes from

Three sources, and which one is used is decided by configuration rather than by trying each in
turn. The key is resolved on the first lookup and cached **per tenant** for
`GeolocationCacheSeconds` — the same TTL a lookup gets — so a key rotated in the store takes effect
within that window without a restart.

1. **Blocks Secrets** (`SeliseBlocks.Secrets.OS`), when `GeolocationApiKeySecretId` names a secret.
   This is the managed path: the key is per tenant, rotatable from the Blocks OS console, and every
   read of it is audited.
2. **The Genesis vault** (`IVault`), under `GeolocationApiKeySecretName`, when no secret id is set.
   The platform-wide key, for a deployment that has not adopted Blocks Secrets.
3. **The `GeolocationApiKey` configuration value**, if neither store answers. This exists so a
   developer without access to either can run the service; it is not the deployment path.

A source that is configured but fails falls through to the next rather than failing the lookup, and
says so in the log. Availability of a geolocation lookup is not worth more than a correct key, but
it is worth more than an outage while a usable key sits one source down.

A provider that needs no key at all (ip-api.com) is a valid configuration: leave all three unset
and no key is sent.

> **The two stores are not interchangeable.** Blocks Secrets keys its Key Vault entries by secret
> id under its own `blocks-secret-` prefix, while the Genesis vault reads a bare secret name. A
> secret created in the Blocks OS console is therefore invisible to source 2, and a vault entry
> provisioned by hand is invisible to source 1. Moving between them means re-entering the key, not
> just changing which setting is present.

#### Using Blocks Secrets

Create the secret once — from the Blocks OS console, or through `ISecretService` — and put the id
it returns in configuration. Store the id, never the value:

```json
{ "GeolocationApiKeySecretId": "3f2a...c91" }
```

`SecretTypes.Service` is the right type: the key is a backend credential with no per-user access
list, so any authenticated caller in the tenant can read it.

Reading a secret requires an authenticated `BlocksContext`, which both endpoints have because they
carry `[Authorize]`. Background work that ever needs a geolocation lookup would have to wrap the
call in `BlocksContext.ExecuteInContext(...)`; nothing in the Worker does today, and a missing
context is logged and falls through to the vault rather than throwing.

Rotating the key means `RotateAsync` on the same secret id — the id in configuration does not
change. The new value is picked up within `GeolocationCacheSeconds`. Every read is audited, so the
cost of that window is one audit row per tenant per window rather than one per lookup; shortening
the TTL trades audit noise for a faster rotation.

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

Payloads are wrapped in the platform's `ApiResponse<T>` envelope, the same one the document
conversion and subscription endpoints use. The data is the lookups themselves.

```json
{
  "success": true,
  "data": [
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
  "error": null,
  "meta": { "correlationId": "0HN7..." }
}
```

Addresses that could not be resolved are omitted from `data`, so it may hold fewer entries than
were asked for. If none resolved, the response is a **404**:

```json
{
  "success": false,
  "data": null,
  "error": {
    "code": "geolocation_not_found",
    "message": "IP address could not be located",
    "traceId": "0HN7..."
  },
  "meta": { "correlationId": "0HN7..." }
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
- The resolved provider key is cached in process for the same TTL, per tenant.
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

### The numeric address form
`startIpNumber` and `lastIpNumber` carry the address as a number, for callers that range-search on
it.

- **IPv4 is exact.** 32 bits fit a double's 53-bit mantissa with room to spare.
- **IPv6 is ordered but not exact.** 128 bits do not fit in a double, so addresses are
  distinguishable only down to roughly a 2^75 granularity: ordering between distant addresses
  holds, two addresses in nearby subnets can share a value. Compare `startIp` when an exact IPv6
  match is needed. Making the number exact means widening the field past `double`, which changes
  the wire contract.
- An IPv4-mapped IPv6 address (`::ffff:8.8.8.8`) is unwrapped first, so the same host gets the same
  number whichever way it was written.

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

Both endpoints carry `[Authorize]`, so both require an authenticated caller.

This is not only about who may see a location. A lookup spends a metered third-party call, and a
cache miss holds the process-wide provider gate for the configured delay — so an anonymous caller
sending ten uncached addresses would occupy that gate for about ten seconds and stall every other
tenant's lookups behind it. The rate limit being shared is exactly what makes the endpoint worth
authenticating.

## Error Handling

| Outcome | Status | `error.code` |
| --- | --- | --- |
| Empty or missing `IpAddresses` | 400 | `geolocation_invalid_request` |
| More than 10 addresses | 400 | `geolocation_invalid_request` |
| No addresses in the request context (`Locate`) | 400 | `geolocation_invalid_request` |
| Nothing resolved | 404 | `geolocation_not_found` |
| Unauthenticated caller | 401 | — |

The status code is the contract. Every outcome used to return HTTP 200 with the failure buried in
the body, which a client's error handling, its gateway and its dashboards all recorded as a
success.

Provider failures — a non-2xx response, a transport error, a timeout, a payload that does not
parse — are absorbed by the repository and reported as an address that could not be located, so
they surface as **404 alongside genuinely unlocatable addresses**. The provider's status code is
logged, so the distinction is recoverable from the logs rather than from the response. Separating
them into a 502 means teaching the repository to report which of the two it hit.

A cancelled request propagates rather than being reported as a failed lookup.

## Testing

### Local testing without a secret store
Set `GeolocationApiUrl` and, for providers that need one, `GeolocationApiKey` in
`appsettings.Development.json`, and leave `GeolocationApiKeySecretId` unset. The vault is tried
first, logs that it found nothing, and the configured key is used.

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

- `ISecretService` — for the provider API key when a secret id is configured
  (`SeliseBlocks.Secrets.OS`; registered by `RegisterUtilityServices`)
- `IServiceScopeFactory` — to open a scope for that scoped `ISecretService`, because this
  repository is a singleton
- `IVault` — for the provider API key when it is not (Genesis; registered by the host)
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
