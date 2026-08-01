# Configuration guide

This operator-facing guide describes configuration and current expression syntax. Intended export/publication behavior is authoritative in the focused contracts.

## Current configuration sources and settings

.NET configuration is loaded from `appsettings.json`, the environment-specific file, .NET User Secrets in Development, and environment variables. Later providers override earlier providers. Environment variables use `__` between sections, for example `Somtoday__ClientId`.

| Setting | Production | Development |
|---|---|---|
| `DOTNET_ENVIRONMENT` | `Production` | Must be explicitly `Development` for local fallbacks |
| `KeyVault__VaultUri` | Required HTTPS URI | Optional |
| `KeyVault__SomtodayClientSecretName` | Optional; default `somtoday-client-secret` | Same |
| `Somtoday__ClientSecret` | Temporary Key Vault bootstrap/rotation only | Effective secret when no Vault URI is configured |
| `Somtoday__ClientId` | Required | Required |
| `Somtoday__SchoolUUID__0` and higher | At least one unique UUID | Same |
| `Somtoday__Environment` | Full `PROD`, `TEST`, or `ACCEPTATIE` preferred | Same, plus `NIGHTLY` for local Development only |
| `Storage__AzureBlob__ServiceUri` | Required | Wins when supplied |
| `Storage__AzureBlob__ConnectionString` | Prohibited | Allowed only when service URI is empty |
| `Storage__AzureBlob__Container` | Required; default `sds` | Same |
| `Output__Folder` | Default `sds/output` | Same |
| `Output__GenerateEmptyCsv` | Default `false` | Same |
| `Output__SeparateByInstitution` | Default `true` | Same |
| `Output__SeparateByLocation` | Default `false` | Same |
| `Locations__IncludedLocationCodes__0` and higher | Optional; empty means all | Same |
| `Locations__ExcludedLocationCodes__0` and higher | Optional; exclusion wins | Same |
| `UsernameFormat__Teacher` | Default `Emailadres` | Same |
| `UsernameFormat__Student` | Default `Emailadres` | Same |
| `SchoolDataSync__EnableGuardianSync` | Default `false` | Same |

Full environment names are preferred for operator clarity. The parser intentionally uses only the first trimmed non-empty character, so minor spelling errors do not cause configuration failure while the four initials remain unique. NIGHTLY uses a plaintext HTTP data endpoint and is rejected unless `DOTNET_ENVIRONMENT=Development`; do not use real personal data with NIGHTLY.

Somtoday institution authentication gets at most four total attempts. Network and HTTP timeout failures, HTTP 408, HTTP 429, and HTTP 5xx responses are retried after a cancellable two-second wait. Other HTTP 4xx responses and invalid token payloads fail immediately. Authentication bodies, access tokens, client secrets, and raw exception messages are not logged.

## Output layout

| Setting | Default |
|---|---|
| `Output__SeparateByInstitution` | `true` |
| `Output__SeparateByLocation` | `false` |

Their output effects are defined only in the [publication contract](../contracts/PUBLICATION.md#output-grouping).

There is no configuration or command-line option to enable, disable, or select an SDS version. Every planned scope always attempts both V1 and V2.1 from the same included population; version-specific publication units exist only to isolate failures.

The default combines all eligible selected locations of each institution into one V1 and one V2.1 dataset. When public matching, discovery, output-layout validation, or data download fails for an institution and grouping combines institutions, the successfully resolved institution subset is still published and the run exits with code `1`. Conversion and upload failures block their complete SDS-version unit but do not suppress the mandatory attempt for the other version or another output scope.

V1 emits a teacher or pupil once per included location. A person belonging to multiple locations therefore receives repeated V1 `SIS ID` values with different `School SIS ID` values, which Microsoft SDS may reject because it documents `SIS ID` as unique. V2.1 emits one user row with separate organization roles.

## Institution and location selection

Use the public production institution list to find UUIDs and abbreviations:

```powershell
Invoke-WebRequest -Uri 'https://api.somtoday.nl/rest/v1/connect/instelling'
```

The application retrieves that list once per run without authentication, even when synchronization uses another Somtoday environment. A configured UUID must occur exactly once and have a usable abbreviation. Failure to retrieve the list aborts the run before dataset publication; an invalid individual match fails that institution while other matched institutions remain eligible.

Location codes are matched case-insensitively. With no inclusion list, every available location is selected. Exclusions always take precedence.

## Username expressions

Teacher and pupil expressions are configured independently. A bare property becomes a user expression automatically: for example, `Emailadres` becomes `{user.Emailadres}`. Other direct values include teacher `Medewerkernummer` and pupil `Leerlingnummer`. A value already beginning with `{user.` and ending with `}` may contain multiple property expressions, literal separators, or supported Dynamic LINQ operations.

For a user with `Voorletters = J`, `Achternaam = Jansen`, `Gebruikersnaam = jjansen`, and `Emailadres = J.Jansen@School.nl`:

| Configured value | Result |
|---|---|
| `Emailadres` | `J.Jansen@School.nl` |
| `Gebruikersnaam` | `jjansen` |
| `{user.Voorletters}.{user.Achternaam}` | `J.Jansen` |
| `{user.Emailadres.ToLower()}` | `j.jansen@school.nl` |
| `{user.Voorletters + "." + user.Achternaam + "@school.nl"}` | `J.Jansen@school.nl` |

Use only properties appropriate for the account type. Generated Somtoday models also expose identifiers and personal data that must not be used as usernames. Validate both formats with representative users before production.

## Key Vault bootstrap and rotation

When `KeyVault__VaultUri` is set, Key Vault is always the effective secret source:

1. If the Vault secret is absent and a temporary `Somtoday__ClientSecret` is supplied, create the initial version.
2. If the temporary value differs, create exactly one new version.
3. If it matches, perform no write and remove the temporary configuration.
4. With no temporary value, use the current Vault version.

Missing values, authorization errors, failed writes, and other Vault failures stop the run with exit code `1`. Never store a real secret or production connection string in a tracked configuration file.

Secret values and OAuth response bodies are not logged. Old Key Vault secret versions remain available for audit or recovery; the application neither rotates the upstream Somtoday credential nor deletes old versions. After a successful bootstrap or rotation, remove the temporary value and redeploy so the corresponding Container Apps secret and environment reference are removed.
