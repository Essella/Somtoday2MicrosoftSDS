# Configuration guide

.NET loads `appsettings.json`, the environment-specific JSON file, Development User Secrets, and environment variables in that order. Environment variables use `__` between sections. Command-line configuration is disabled.

| Setting | Required/default |
|---|---|
| `Somtoday__SchoolUUID__0` and higher | Required non-empty array of unique institution UUIDs |
| `Somtoday__ClientId` | Required |
| `Somtoday__ClientSecret` | Required; use the Job secret or Development User Secrets |
| `Somtoday__Environment` | `PROD` by default; `TEST` and `ACCEPTATIE` supported; `NIGHTLY` Development-only |
| `SchoolDataSync__InboundFlowId` | Required UUID of exactly one SDS inbound flow |
| `Locations__IncludedLocationCodes__0` and higher | Optional; empty means all locations |
| `Locations__ExcludedLocationCodes__0` and higher | Optional; exclusion wins |
| `UsernameFormat__Teacher` | `Emailadres` |
| `UsernameFormat__Student` | `Emailadres` |
| `SchoolDataSync__EnableGuardianSync` | `false` |

Do not configure a connector ID or CSV version. The application resolves the connector from the inbound flow and maps `schoolDataSyncV1` to V1 and `schoolDataSyncV2Rev1` to V2.1. One run combines all successful configured schools into one complete selected-format dataset. A failed school is omitted, but makes the final exit code `1`.

Header-only mode is enabled automatically on July 31 in `Europe/Amsterdam`. It still resolves the connector and discovers configured schools before uploading the complete header-only set.

## Username expressions

A bare property such as `Emailadres` becomes `{user.Emailadres}`. Expressions may contain literals, multiple property expressions, and supported Dynamic LINQ operations, for example `{user.Emailadres.ToLower()}` or `{user.Voorletters}.{user.Achternaam}`. Treat expressions as trusted administrator configuration. Do not use BSN/ECK identifiers, phone numbers, dates, nested objects, or other sensitive fields.

Teacher and pupil export is matching-only. Configure SDS not to create unmatched accounts.

## Authentication and secrets

Production Graph access uses `DefaultAzureCredential`, constrained by infrastructure to `ManagedIdentityCredential`. Local development may use any supported `DefaultAzureCredential` developer credential with the required Graph application permissions: `IndustryData-InboundFlow.ReadWrite.All`, `IndustryData-DataConnector.Upload`, and `IndustryData.ReadBasic.All`. Never track the Somtoday secret, Azure tokens, SAS URLs, or production data.

Somtoday authentication has four total attempts and retries only network/timeouts, HTTP 408/429, and HTTP 5xx. Other 4xx responses and invalid token payloads fail immediately.
