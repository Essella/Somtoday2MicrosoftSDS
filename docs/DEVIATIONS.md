# Known implementation deviations

This register is the single source for known differences between confirmed intent and current implementation. The **Current behavior** column contains code-observed facts; the linked contract contains confirmed intent. IDs are stable: do not reuse or renumber them. A deviation is a known implementation gap, not an unanswered requirement. Resolved IDs remain recorded separately and must not be reused.

## Active deviations

None.

## Resolved deviations

| ID | Resolved | Resolution |
|---|---|---|
| `DEV-014` | 2026-08-01 | Exact trailing staging-path and producer-metadata recognition protects live output paths containing `.staging`; invalid, missing, non-v7, and nested run paths remain untouched. |
| `DEV-015` | 2026-08-01 | Guardian phone normalization now emits only exact ASCII E.164 or an empty value in both SDS versions without removing the guardian or relationships. |
| `DEV-016` | 2026-08-01 | NIGHTLY is now rejected outside Development and removed from both the Bicep production parameter and its regenerated ARM template. |
| `DEV-017` | 2026-08-01 | Authentication now returns an internal transient/permanent result, makes at most four total attempts, retries only network/HTTP timeout, 408, 429, and 5xx failures, and preserves cancellable two-second waits. |
| `DEV-018` | 2026-08-01 | Startup and dataset staging cleanup now make four complete best-effort attempts, try every applicable Blob, preserve primary publication/rollback/cancellation behavior, and warn without changing successful publication or run continuation. |
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

## Detailed resolution records for the 2026-08-01 audit

| ID | Intent | Pre-fix behavior and evidence | Impact | Status, chosen action, and verification | Remaining assumption |
|---|---|---|---|---|---|
| `DEV-014` | Delete only exact application-owned run staging. | `DatasetPublisher.IsOwnedStagingBlob` used `Contains("/.staging/")` plus producer metadata. | Configurable live paths could be deleted. | Resolved by exact segment, compact UUIDv7 version/variant, filename, and metadata validation. Regression tests cover real staging, invalid/missing/non-v7 IDs, extra segments, missing metadata, and `.staging` at configurable live path levels. | Older staging conventions may remain. |
| `DEV-015` | Export exact E.164 or empty while retaining guardian data. | Repeated plus signs were stripped for validation and Unicode digits were accepted through `char.IsDigit`. | Optional phones could invalidate complete SDS datasets. | Resolved with an ASCII anchored final validation. Tests cover Dutch and `00` conversion, exact lower/upper length bounds, plus errors, Unicode digits, letters, invalid country code, and equal empty V1/V2.1 output with relationships retained. | Newly rejected source values intentionally produce empty phone output. |
| `DEV-016` | Accept NIGHTLY only in Development and keep it out of production infrastructure parameters. | Runtime configuration accepted NIGHTLY in every environment, and Bicep and generated ARM exposed it while the endpoint uses HTTP. | A production job could send bearer-authenticated school data over plaintext HTTP. | Resolved by the environment guard and by removing NIGHTLY from Bicep. The ARM template was regenerated and the Bicep source and parameter file were compiled with the official standalone Bicep CLI v0.45.15; tests cover Production rejection and Development acceptance. | Local Development remains responsible for using no real personal data with NIGHTLY. |
| `DEV-017` | Retry transient authentication only, four total attempts. | A 20-retry loop treated every unsuccessful connection alike. | Permanent failures were delayed and transient retry was excessive. | Resolved with `Succeeded`, `TransientFailure`, and `PermanentFailure` outcomes. Tests cover 400/401, 408/429/5xx, network failure, timeout, later recovery, invalid payload, delay duration, and cancellation. | Somtoday data-endpoint retries remain outside scope. |
| `DEV-018` | Make cleanup four-attempt best effort without changing primary status. | Startup tried once, dataset Blobs once each, and Azure deletion stopped on the first failure. | More staging remained and cleanup failure semantics were not protected. | Resolved by complete retries, per-attempt aggregation, and explicit preservation of an in-flight primary exception. Tests cover later success, exhausted startup and dataset warnings, all known Blobs after an individual failure, publication success, primary rollback failure, and preservation of the original cancellation exception. | Staging can remain until a later startup or lifecycle deletion; overlapping runs remain unsupported. |

## Audit evidence

The 2026-07-30 audit inspected solution/project files, startup, configuration, dependency injection, helpers, CSV models, tests, Dockerfile, Bicep, scripts, workflows, Markdown, generated-client headers, OpenAPI metadata, and recent Git history.

It did not inspect every generated line of `openapi.cs`, every schema entry in `openapi.json`, or generated ARM JSON. `dotnet test .\Somtoday2MicrosoftSDS.sln --configuration Release` passed 38 tests with no failures or skips.

The audit read the public Somtoday institution endpoint and official Microsoft documentation. It did not access an authenticated Somtoday environment, Azure subscription, production Blob data, or Microsoft SDS tenant.

The 2026-08-01 remediation audit restored the solution and ran the complete Release test suite: 128 tests passed and the seven credential-dependent live Somtoday tests were skipped. The Release build with warnings treated as errors succeeded with zero warnings, and `git diff --check` succeeded. The OpenAPI specification and generated client were unchanged. The official standalone Bicep CLI v0.45.15 compiled `infra/main.bicep` and `infra/main.example.bicepparam`, regenerated `infra/azuredeploy.json`, and confirmed that NIGHTLY is absent from the production Bicep and ARM parameter. No authenticated Somtoday, Azure-subscription, production Blob, or Microsoft SDS ingest test was performed.
