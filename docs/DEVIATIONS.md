# Known implementation deviations

This register is the single source for known differences between confirmed intent and current implementation. The **Current behavior** column contains code-observed facts; the linked contract contains confirmed intent. IDs are stable: do not reuse or renumber them. A deviation is a known implementation gap, not an unanswered requirement. Resolved IDs remain recorded separately and must not be reused.

## Active deviations

| ID | Area | Current behavior | Intended reference |
|---|---|---|---|
| `DEV-003` | Output layout | Output always uses `{OutputPrefix}/{SanitizedInstitutionAbbreviation}/{SanitizedLocationAbbreviation}/v1|v2/{FileName}`; the two layout settings and location-only collision rule are absent. | [Publication grouping](contracts/PUBLICATION.md#output-grouping) |
| `DEV-004` | Institution naming | Institution discovery uses the authenticated configured environment instead of the public production institution endpoint. | [Institution abbreviation source](contracts/PUBLICATION.md#institution-abbreviation-source) |
| `DEV-006` | Publication | Files overwrite live Blob output individually; complete staging, destination snapshots, retry/timeout, promotion rollback, and global storage-stop behavior are absent. | [Publication unit and staging](contracts/PUBLICATION.md#publication-unit-and-staging) |
| `DEV-007` | Guardian file lifecycle | Disabling guardian sync does not remove prior guardian files; enabled sync skips empty guardian files instead of replacing them with header-only files. | [Guardian file lifecycle](contracts/PUBLICATION.md#guardian-file-lifecycle) |

## Resolved deviations

| ID | Resolved | Resolution |
|---|---|---|
| `DEV-001` | 2026-07-30 | The release workflow publishes only the supported `linux/amd64` container image and no Windows archive. |
| `DEV-002` | 2026-07-30 | Releases no longer depend on the `OPENAPI_REDISTRIBUTION_CONFIRMED` repository variable. |
| `DEV-005` | 2026-07-31 | The owner-confirmed availability intent was revised: the adapter now returns every selected location, normal mode resolves population once and skips only locations without an exportable class, and a skip warns without changing existing output or failing the run. Header-only mode remains independent. |
| `DEV-008` | 2026-07-31 | V1 and V2.1 now consume one resolved population, so unassigned employees and pupils are absent from users, roles, enrollments, and other exported files. |
| `DEV-009` | 2026-07-31 | Class membership is now resolved by UUID before eligibility is decided; blank-named classes are excluded, unresolved references are ignored, and every included class retains at least one resolved teacher and pupil. |
| `DEV-010` | 2026-07-30 | Each run captures one `Europe/Amsterdam` date for the July 31 trigger and both converters' school-year calculation. |
| `DEV-011` | 2026-07-30 | V2.1 guardian users include the confirmed given name, family name, and email mappings. |
| `DEV-012` | 2026-07-30 | V1 guardian first and last names follow the confirmed initials and joined-prefix mapping. |
| `DEV-013` | 2026-07-30 | Both converters share consent, email, phone-preference, normalization, and secret-number filtering rules. |

### DEV-004 verification

**Code-observed fact, verified 2026-07-30:** `SyncConfiguration` selects a `SomEnvironmentConfig`; `OpenAPIHelper.ConnectAsync` assigns that environment's `Url` to the generated client's `BaseUrl`; and `GetInstellingAsync` retrieves the institution through that same client. The application does not make a separate request to the public production endpoint for `Instelling.Afkorting`.

## Audit evidence

The 2026-07-30 audit inspected solution/project files, startup, configuration, dependency injection, helpers, CSV models, tests, Dockerfile, Bicep, scripts, workflows, Markdown, generated-client headers, OpenAPI metadata, and recent Git history.

It did not inspect every generated line of `openapi.cs`, every schema entry in `openapi.json`, or generated ARM JSON. `dotnet test .\Somtoday2MicrosoftSDS.sln --configuration Release` passed 38 tests with no failures or skips.

The audit read the public Somtoday institution endpoint and official Microsoft documentation. It did not access an authenticated Somtoday environment, Azure subscription, production Blob data, or Microsoft SDS tenant.
