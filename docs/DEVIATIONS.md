# Known implementation deviations

This register is the single source for known differences between confirmed intent and current implementation. The **Current behavior** column contains code-observed facts; the linked contract contains confirmed intent. IDs are stable: do not reuse or renumber them. A deviation is a known implementation gap, not an unanswered requirement. Resolved IDs remain recorded separately and must not be reused.

## Active deviations

None.

## Resolved deviations

| ID | Resolved | Resolution |
|---|---|---|
| `DEV-006` | 2026-08-01 | Complete in-memory datasets are staged once, promoted by server-side copy with one attempt plus three complete-set retries, and restored from the newest older complete application-authored Blob-version group after exhausted promotion. Missing or failed rollback stops later datasets. |
| `DEV-007` | 2026-08-01 | Guardian-enabled datasets always publish their guardian-specific files, including header-only files, while guardian-disabled promotion removes the known guardian files and rollback restores the selected older set's guardian state. |
| `DEV-003` | 2026-07-31 | Both output-layout settings now plan the four confirmed dataset scopes, including location-only collision disambiguation and grouped location conversion. |
| `DEV-004` | 2026-07-31 | Each run now retrieves the institution list once from the unauthenticated public production endpoint and uses the matching public abbreviation independently of the configured synchronization environment. |
| `DEV-001` | 2026-07-30 | The release workflow publishes only the supported `linux/amd64` container image and no Windows archive. |
| `DEV-002` | 2026-07-30 | Releases no longer depend on the `OPENAPI_REDISTRIBUTION_CONFIRMED` repository variable. |
| `DEV-005` | 2026-07-31 | The adapter returns every selected location, normal mode resolves population once and omits locations without an exportable class, and only a publication scope with no eligible location is skipped without changing existing output. Header-only mode remains independent. |
| `DEV-008` | 2026-07-31 | V1 and V2.1 now consume one resolved population, so unassigned employees and pupils are absent from users, roles, enrollments, and other exported files. |
| `DEV-009` | 2026-07-31 | Class membership is now resolved by UUID before eligibility is decided; blank-named classes are excluded, unresolved references are ignored, and every included class retains at least one resolved teacher and pupil. |
| `DEV-010` | 2026-07-30 | Each run captures one `Europe/Amsterdam` date for the July 31 trigger and both converters' school-year calculation. |
| `DEV-011` | 2026-07-30 | V2.1 guardian users include the confirmed given name, family name, and email mappings. |
| `DEV-012` | 2026-07-30 | V1 guardian first and last names follow the confirmed initials and joined-prefix mapping. |
| `DEV-013` | 2026-07-30 | Both converters share consent, email, phone-preference, normalization, and secret-number filtering rules. |

## Audit evidence

The 2026-07-30 audit inspected solution/project files, startup, configuration, dependency injection, helpers, CSV models, tests, Dockerfile, Bicep, scripts, workflows, Markdown, generated-client headers, OpenAPI metadata, and recent Git history.

It did not inspect every generated line of `openapi.cs`, every schema entry in `openapi.json`, or generated ARM JSON. `dotnet test .\Somtoday2MicrosoftSDS.sln --configuration Release` passed 38 tests with no failures or skips.

The audit read the public Somtoday institution endpoint and official Microsoft documentation. It did not access an authenticated Somtoday environment, Azure subscription, production Blob data, or Microsoft SDS tenant.
