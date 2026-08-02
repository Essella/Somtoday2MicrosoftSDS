# Architecture and data flow

This document separates confirmed intended component boundaries from code-observed execution. Intended export and publication rules live in the focused contracts, not here.

## Intended component boundaries

### `Program`: process orchestration

Owns the run lifecycle, composition root, institution isolation, output-scope and dataset order, fatal storage-stop behavior, path-collision checks, mode selection, grouped conversion/publication sequencing, school failure accounting, and final exit code.

- **Inputs:** Exact `--empty-csv` command-line detection, host configuration that deliberately excludes command-line configuration, current date, dependency-injection services, and application cancellation.
- **Side effects and outputs:** Logs and a process exit code; Somtoday and Blob effects are delegated to their owning components.
- **Boundary:** Does not implement field-level SDS mapping or raw API transport.

### `SyncConfiguration` and `SettingsHelper`: validated settings

`SyncConfiguration` builds immutable run settings and validates required Somtoday and Blob configuration, including HTTPS for every configured Blob service URI. The plaintext Somtoday NIGHTLY environment is accepted only in Development. `SettingsHelper` stores and evaluates process-global username rules. `SettingsHelper.Initialize` changes process-wide static formatter state.

- **Inputs:** Layered .NET configuration, including the directly configured client secret.
- **Outputs:** An immutable `SyncConfiguration`, validation errors, and compiled username accessors.
- **Boundary:** Does not retrieve secrets from an external store or test external connectivity.

### `OpenAPIHelper` and generated client: Somtoday adapter

Retrieve the public production institution list without authentication, authenticate for one institution against the configured environment, configure the generated client, page through current-school-year data, apply location selection, and assemble one `VestigingModel` for every selected location.

- **Inputs:** Credentials, institution UUID, the public production institution endpoint, selected Somtoday environment endpoints, location selection, guardian flag, and cancellation.
- **Side effects and outputs:** Somtoday HTTP requests and in-memory `VestigingModel` values, including one for each selected location when source collections are empty.
- **Boundary:** Does not create CSV or write Blob output.

The project owner generated `OpenAPIs/openapi.cs` with Visual Studio from the tracked OpenAPI 3.0.3 specification version 14.10.0. Future generated-client changes require explicit scope plus recorded source, specification version, and generation tool/version.

### Export population resolver, SDS transformers, and CSV models

`ExportPopulationResolver` resolves class membership once per location and creates the shared class, teacher, pupil, and guardian population used by both SDS versions. It also returns a diagnostic count of guardians that otherwise qualify but lack required name fields; `Program` logs only that count once per location. `SDScsvHelperV1` and `SDScsvHelperV2` map the ordered resolved populations in one publication scope and the username rules into one in-memory CSV dataset. Detailed intended mapping is in the [export contract](contracts/EXPORT.md).

- **Inputs:** The resolver receives one `VestigingModel`; the transformers receive an ordered collection of `ResolvedExportPopulation` values, the run's captured Amsterdam date, and the process-global username formatters.
- **Output:** One shared resolved population followed by in-memory V1 and V2.1 CSV model collections.
- **Boundary:** Does not retrieve missing entities, perform external I/O, or validate the finished dataset against an external SDS service.

### `FileHelper`: complete CSV serialization

Serializes every file of one V1 or V2.1 publication unit to memory before Blob I/O starts. It includes header-only guardian files whenever guardian sync is enabled and centrally rejects CR or LF in every string property mapped by the active CSV class map.

- **Inputs:** SDS record collections and the guardian setting.
- **Output:** One complete in-memory `PublicationDataset` with the known core and guardian file names.
- **Boundary:** Has no external I/O and does not select institutions, locations, staging paths, or rollback state. A wire-validation error identifies only SDS version, file, and column and is raised before the affected version can stage data.

### `DatasetPublisher`, Blob store, `BlobClientFactory`, and `BlobPathHelper`: Blob publication

Select Blob authentication, normalize prefixes, create the container, stage a complete dataset, perform server-side complete-set promotion retries, find complete application-authored groups among base Blobs and Blob versions, restore after exhausted promotion, clean staging, and manage guardian-file lifecycle. Detailed behavior is in the [publication contract](contracts/PUBLICATION.md).

- **Inputs:** Validated storage settings, the normalized output prefix, one `PublicationDataset`, its independent live prefix, the captured run timestamp, the run's UUIDv7 identifier, and cancellation.
- **Side effects:** Container creation, staging uploads, live server-side copies, base/version rollback, staging cleanup, and known guardian-file removal.
- **Boundary:** Does not serialize CSV, select institutions or locations, decide export population, or decide whether a recoverable dataset failure stops later datasets.

### Infrastructure and delivery

`infra/main.bicep` is the source for Azure resources. It provisions Blob Storage, Log Analytics, a Container Apps environment, a scheduled Job with its Somtoday secret, and a narrow Blob role assignment. The Dockerfile builds the supported non-root Linux image. Workflows validate and release the project.

Infrastructure does not provision Somtoday access, a Microsoft SDS ingestion configuration, a Power Automate flow, an SDS Flow ID, an Entra application registration, a Power Automate Reader-role assignment, or operator privacy governance. It keeps hierarchical namespace and Azure Blob SFTP disabled so publication can continue to depend on Blob versioning.

## Current observed execution

1. `Program` creates the generic host without passing command-line arguments into host configuration, then creates the logger, HTTP client factory, and `FileHelper`. It separately detects exact, case-insensitive `--empty-csv` arguments; all other arguments are ignored.
2. `SyncConfiguration` reads and validates the directly configured client secret and run settings; `SettingsHelper` initializes username expressions separately.
3. `BlobClientFactory` creates the authenticated Blob context. The Blob store creates the container if needed, and `DatasetPublisher` recognizes exact application-owned UUIDv7 staging paths and makes up to four complete best-effort cleanup attempts. Exhausted cleanup warns and startup continues.
4. The process retrieves the public production institution list once without authentication and matches each configured institution UUID.
5. For each matched institution, the process authenticates against the configured environment with at most four transient-only attempts and a cancellable two-second retry wait, selects locations, and creates the complete output-layout plan before population eligibility is evaluated. An empty inclusion list retains a blank-abbreviation location so layout validation records that institution as failed instead of silently omitting the location.
6. Locations within an institution and institutions themselves are downloaded sequentially. Group, employee, pupil, and optional guardian requests run concurrently within one location.
7. `ExportPopulationResolver` resolves one shared population per location, removes guardians without required contact, relevant pupil relationship, initials, or surname, and returns a safe missing-name exclusion count. `Program` logs only that count once per location. Normal mode warns and omits locations without an included class; source-level institution failures are excluded from combined scopes while successfully resolved institutions remain publishable.
8. For every planned output scope, the V1 and V2 helpers are both invoked with the same remaining ordered location populations, captured Amsterdam date, and guardian export policy. Neither version has an exclusion path. A conversion failure blocks that versioned publication unit but does not suppress the mandatory attempt for the other version or later output scopes.
9. `Program` creates one compact UUIDv7 run identifier. `FileHelper` validates mapped string fields and serializes the complete UTF-8 CSV set without a byte-order mark. `DatasetPublisher` stages the dataset at `{OutputPrefix}/.staging/{RunId}/{FileName}`, independently promotes it to its live prefix with at most four complete-set attempts, and uses application metadata from live base Blobs and Blob versions for complete-set rollback after exhausted promotion. Staging is never a rollback source. A selected versionless base source already at its destination is a no-op; versioned sources use server-side copy. Dataset staging cleanup also gets four complete best-effort attempts and never changes a successful publication after exhaustion. Dataset units execute sequentially, so a later unit overwrites every reused staging name before promotion even when earlier cleanup was exhausted.
10. A successful rollback isolates the publication failure to that dataset; missing or failed rollback propagates a fatal exception so `Program` stops later datasets. Application cancellation bypasses rollback.
11. The process returns `0` only when no institution was recorded as failed; configuration, cancellation, secret, storage, discovery, data-download, layout, conversion, or publication failures return `1`.

## Current constraints and observations

- At least one unique valid institution UUID, a client ID, and an effective secret are required.
- A directly configured Somtoday client secret and an HTTPS Blob service URI are required outside Development; Blob connection strings are rejected. In Development, an HTTPS service URI takes precedence over a connection string, while local HTTP emulators use a connection string.
- Location matching is case-insensitive. Empty inclusion means all locations, including blank-abbreviation locations that subsequently fail their institution during layout validation; a non-empty inclusion list cannot match a blank abbreviation. Exclusion wins over inclusion for usable codes.
- Institution and location abbreviations must remain non-empty after normalization. Slash and backslash become `_`, and paths are compared case-insensitively.
- The first live segment below `Output:Folder` cannot equal the reserved `.staging` segment in any casing. The affected institution fails through output-layout validation.
- Output paths follow both independent layout settings. Paths are compared case-insensitively; location-only cross-institution collisions receive an institution prefix, while unresolved collisions fail the affected institution.
- `OpenAPIHelper` returns every selected location aggregate, including aggregates with empty source collections. Normal mode excludes locations without an included class from their planned scope; existing Blob output is left unchanged only when the complete scope has no eligible location.
- Institution authentication gets at most four total attempts. Network and HTTP timeout failures, HTTP 408, HTTP 429, and HTTP 5xx responses are transient; other HTTP 4xx responses and invalid authentication payloads are permanent. Only transient failures are retried, with the existing cancellable two-second application delay.
- Class identifiers retain their version-specific prefix checks: V1 checks the unfiltered group name, V2.1 checks the filtered group name, and both emit the filtered name with the Amsterdam school-year suffix.
- A public-matching, discovery, output-layout, or data-download failure in one institution does not prevent unaffected institutions from being attempted or published. Combined scopes publish the successful subset, and any recorded institution failure makes the process exit with `1`.
- Grouped conversion preserves each version's existing class identifier formula and blocks a versioned dataset when different source classes produce the same case-insensitive identifier. V1 retains one teacher or pupil row per location; V2.1 deduplicates one source person across locations and retains organization roles.
- Authentication bodies, tokens, secrets, API bodies, and raw exception messages are excluded from application error summaries by current tests.
- Cancellation flows through retries, HTTP, and Blob operations and results in exit code `1`.
- Publication uses the Azure Blob SDK defaults and adds no operation timeout or delay. Staging cleanup gets one initial complete attempt plus three complete retries, tries every applicable Blob in each attempt, and remains best effort after exhaustion. Cleanup exhaustion warns without changing publication success, live output, institution status, or run continuation. Exact run-ID path recognition protects live paths containing a `.staging` segment. Startup staging cleanup makes overlapping application runs unsupported.
- Production infrastructure uses one normalized output-folder value for runtime configuration and the staging lifecycle prefix. It makes staging base Blobs and staging versions eligible for lifecycle deletion after more than one day while retaining the general seven-day previous-version rule; seven-day Blob soft delete can retain lifecycle-deleted data temporarily beyond those points.
- The username formatter is trusted-administrator configuration, not a sandbox. It accepts every public property on the generated `Medewerker` or `Leerling` model. The types include unsuitable or sensitive fields, and their property sets differ.
- Examples of exposed but unsuitable or sensitive properties include BSN/ECK identifiers, dates, phone numbers, and nested objects.
- Composite username text must currently start with `{user.` and end with `}` or normalization treats the entire value as one property. Bare properties, multiple expressions with literals, the documented `ToLower()` example, and the documented concatenation example have regression coverage; other Dynamic LINQ operations and null or nested-property behavior remain undefined.

## Known deviations

Use [the deviation register](DEVIATIONS.md) for current gaps. Do not restate deviation details in architecture changes; reference their stable IDs.
