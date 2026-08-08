# Configuration guide

.NET loads `appsettings.json`, the environment-specific JSON file, Development User Secrets, and environment variables in that order. Environment variables use `__` between sections. Command-line configuration is disabled.

| Setting | Required/default |
|---|---|
| `Somtoday__SchoolUUID__0` and higher | Required non-empty array of unique institution UUIDs |
| `Somtoday__ClientId` | Required |
| `Somtoday__ClientSecret` | Required opaque value; use the Job secret or Development User Secrets |
| `Somtoday__Environment` | `PROD` by default; `TEST` and `ACCEPTATIE` supported; `NIGHTLY` Development-only |
| `SchoolDataSync__SourceName` | Required non-empty immutable name of exactly one SDS CSV source |
| `Locations__IncludedLocationCodes__0` and higher | Optional; empty means all locations |
| `Locations__ExcludedLocationCodes__0` and higher | Optional; exclusion wins |
| `UsernameFormat__Teacher` | `Emailadres` |
| `UsernameFormat__Student` | `Emailadres` |
| `SchoolDataSync__EnableGuardianSync` | `false` |

The Azure CLI Job template accepts institution UUIDs and location codes as comma-separated values. The Bicep deployment trims each value, removes double quotes from institution UUIDs, and removes empty optional location-code entries before it creates the Job environment variables.

Do not configure a connector ID or CSV version. Configure the source name that the SDS administrator set for the CSV source. The application lists Graph data connectors and requires exactly one connector with an exact matching display name. It maps `schoolDataSyncV1` to V1 and `schoolDataSyncV2Rev1` to V2.1. One run combines all successful configured schools into one complete selected-format dataset. A failed school is omitted, but makes the final exit code `1`.

After trimming, the first character of `Somtoday__Environment` selects `PROD`, `TEST`, `ACCEPTATIE`, or `NIGHTLY` case-insensitively. A single `P`, `T`, `A`, or `N` is therefore sufficient. Use the complete word in configuration for readability. Every value selected through `N` is Development-only.

Header-only mode is enabled automatically on July 31 in `Europe/Amsterdam`. It still resolves the connector and discovers configured schools before uploading the complete header-only set.

## Username expressions

A bare property such as `Emailadres` becomes `{user.Emailadres}`. A template can combine literal text, multiple property expressions, and supported Dynamic LINQ string operations. Examples are `idm-{user.Emailadres}`, `{user.Emailadres}-student`, `{user.Emailadres.Split("@")[0]}@school.nl`, and `{user.Emailadres.Replace("@old.example", "@new.example")}`.

Startup validates the template structure and compiles and caches the teacher and pupil formatters without executing them against synthetic source data. Dataset construction evaluates the compiled formatter for each included person. A data-dependent expression failure stops construction before an upload session is requested. Use a conditional expression when source data can omit a required delimiter, for example `{user.Emailadres != null && user.Emailadres.Contains("@") ? user.Emailadres.Split("@")[0] : ""}`.

Treat expressions as trusted administrator configuration. Do not use BSN/ECK identifiers, phone numbers, dates, nested objects, or other sensitive fields.

Teacher and pupil export is matching-only. Configure SDS not to create unmatched accounts.

## Authentication and secrets

Production Graph access uses `DefaultAzureCredential`, constrained by infrastructure to `ManagedIdentityCredential`. Local development may use any supported `DefaultAzureCredential` developer credential with the required Graph application permissions: `IndustryData-DataConnector.Read.All`, `IndustryData-DataConnector.Upload`, and `IndustryData.ReadBasic.All`. Never track the Somtoday secret, Azure tokens, SAS URLs, or production data.

Somtoday authentication has four total attempts and retries only network/timeouts, HTTP 408/429, and HTTP 5xx. Other 4xx responses and invalid token payloads fail immediately.

The client secret is rejected when it contains only whitespace. Every other value is passed to Somtoday exactly as configured; the application does not trim or otherwise normalize it. Somtoday authentication, public discovery, and authenticated data requests do not follow redirects. A 3xx response fails the affected operation without contacting its redirect target.
