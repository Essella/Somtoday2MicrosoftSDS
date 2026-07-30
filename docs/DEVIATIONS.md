# Known implementation deviations

This register is the single source for known differences between confirmed intent and current implementation. The **Current behavior** column contains code-observed facts; the linked contract contains confirmed intent. IDs are stable: do not reuse or renumber them. A deviation is a known implementation gap, not an unanswered requirement.

| ID | Area | Current behavior | Intended reference |
|---|---|---|---|
| `DEV-001` | Release | The workflow creates and publishes a framework-dependent Windows archive. | [Core supported runtime](PROJECT_CORE.md#purpose-and-scope), [release guide](operations/RELEASES.md#supported-artifact) |
| `DEV-002` | Release | The workflow blocks releases unless `OPENAPI_REDISTRIBUTION_CONFIRMED=true`, although no additional approval is required. | [Release guide](operations/RELEASES.md#supported-artifact) |
| `DEV-003` | Output layout | Output always uses `{OutputPrefix}/{SanitizedInstitutionAbbreviation}/{SanitizedLocationAbbreviation}/v1|v2/{FileName}`; the two layout settings and location-only collision rule are absent. | [Publication grouping](contracts/PUBLICATION.md#output-grouping) |
| `DEV-004` | Institution naming | Institution discovery uses the authenticated configured environment instead of the public production institution endpoint. | [Institution abbreviation source](contracts/PUBLICATION.md#institution-abbreviation-source) |
| `DEV-005` | Source availability | A selected location is omitted unless groups, employees, and pupils are all non-empty; guardian-enabled runs also require a guardian. | [Export availability](contracts/EXPORT.md#dataset-formats-and-availability) |
| `DEV-006` | Publication | Files overwrite live Blob output individually; complete staging, destination snapshots, retry/timeout, promotion rollback, and global storage-stop behavior are absent. | [Publication unit and staging](contracts/PUBLICATION.md#publication-unit-and-staging) |
| `DEV-007` | Guardian file lifecycle | Disabling guardian sync does not remove prior guardian files; enabled sync skips empty guardian files instead of replacing them with header-only files. | [Guardian file lifecycle](contracts/PUBLICATION.md#guardian-file-lifecycle) |
| `DEV-008` | V2 population | V2 exports every downloaded employee and pupil as a user/role, including people without an included class. | [Class and person population](contracts/EXPORT.md#class-and-person-population) |
| `DEV-009` | Class eligibility | Both converters test source references before resolving them, so a class can be emitted without both an exported teacher and exported pupil. | [Class and person population](contracts/EXPORT.md#class-and-person-population) |
| `DEV-010` | Time | School-year and July 31 logic use the process-local clock; the supplied Linux container normally uses UTC. | [Core invariant](PROJECT_CORE.md#key-invariants), [export availability](contracts/EXPORT.md#dataset-formats-and-availability) |
| `DEV-011` | V2 guardians | V2 guardian users omit `givenName`, `familyName`, and `email`. | [Guardian mapping](contracts/EXPORT.md#guardian-inclusion-and-mapping) |
| `DEV-012` | V1 guardians | With a prefix, V1 places prefix plus surname in `First Name` and omits the prefix from `Last Name`. | [Guardian mapping](contracts/EXPORT.md#guardian-inclusion-and-mapping) |
| `DEV-013` | Guardian consent and phone privacy | Both converters include guardians based on a non-empty email address without checking `WenstContactViaEMail`. They normalize the fallback phone directly without applying the confirmed secret-number suppression or mobile/home/work preference. | [Guardian mapping](contracts/EXPORT.md#guardian-inclusion-and-mapping) |

### DEV-004 verification

**Code-observed fact, verified 2026-07-30:** `SyncConfiguration` selects a `SomEnvironmentConfig`; `OpenAPIHelper.ConnectAsync` assigns that environment's `Url` to the generated client's `BaseUrl`; and `GetInstellingAsync` retrieves the institution through that same client. The application does not make a separate request to the public production endpoint for `Instelling.Afkorting`.

## Audit evidence

The 2026-07-30 audit inspected solution/project files, startup, configuration, dependency injection, helpers, CSV models, tests, Dockerfile, Bicep, scripts, workflows, Markdown, generated-client headers, OpenAPI metadata, and recent Git history.

It did not inspect every generated line of `openapi.cs`, every schema entry in `openapi.json`, or generated ARM JSON. `dotnet test .\Somtoday2MicrosoftSDS.sln --configuration Release` passed 38 tests with no failures or skips.

The audit read the public Somtoday institution endpoint and official Microsoft documentation. It did not access an authenticated Somtoday environment, Azure subscription, production Blob data, or Microsoft SDS tenant.
