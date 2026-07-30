# Architecture and data flow

This document separates confirmed intended component boundaries from code-observed execution. Intended export and publication rules live in the focused contracts, not here.

## Intended component boundaries

### `Program`: process orchestration

Owns the run lifecycle, composition root, institution isolation, global storage-stop behavior, retry and rollback orchestration, path-collision checks, mode selection, conversion/upload sequencing, and final exit code.

- **Inputs:** Command-line arguments, host configuration, current date, dependency-injection services, and application cancellation.
- **Side effects and outputs:** Logs and a process exit code; Somtoday, Key Vault, and Blob effects are delegated to their owning components.
- **Boundary:** Does not implement field-level SDS mapping or raw API transport.

### `SyncConfiguration` and `SettingsHelper`: validated settings

`SyncConfiguration` builds immutable run settings and validates required Somtoday and Blob configuration. `SettingsHelper` stores and evaluates process-global username rules. `SettingsHelper.Initialize` changes process-wide static formatter state.

- **Inputs:** Layered .NET configuration and the already-resolved client secret.
- **Outputs:** An immutable `SyncConfiguration`, validation errors, and compiled username accessors.
- **Boundary:** Does not retrieve secrets or test external connectivity.

### `SomtodaySecretProvider`: secret lifecycle

Resolves the effective Somtoday client secret. With Key Vault configured, it may create the initial Vault secret or add a new version from a temporary bootstrap/rotation value. Secret-resolution failures stop the run.

- **Inputs:** Vault URI, secret name, temporary bootstrap value, Development secret, environment mode, and cancellation.
- **Side effects and output:** Returns the effective secret; may read or write Key Vault; logs lifecycle status without secret values.
- **Boundary:** Does not rotate the upstream Somtoday credential or delete old Vault versions.

### `OpenAPIHelper` and generated client: Somtoday adapter

Authenticate for one institution, configure the generated client, page through current-school-year data, apply location selection, and assemble one `VestigingModel` for every selected location.

- **Inputs:** Credentials, institution UUID, selected Somtoday environment endpoints, location selection, guardian flag, and cancellation.
- **Side effects and outputs:** Somtoday HTTP requests and in-memory `VestigingModel` values, including one for each selected location when source collections are empty.
- **Boundary:** Does not create CSV or write Blob output.

The project owner generated `OpenAPIs/openapi.cs` with Visual Studio from the tracked OpenAPI 3.0.3 specification version 14.10.0. Future generated-client changes require explicit scope plus recorded source, specification version, and generation tool/version.

### SDS transformers and CSV models

`SDScsvHelperV1` and `SDScsvHelperV2` map location aggregates and username rules into in-memory CSV record collections. Detailed intended mapping is in the [export contract](contracts/EXPORT.md).

- **Inputs:** One `VestigingModel`, the run's captured Amsterdam date, and the process-global username formatters.
- **Output:** In-memory V1 or V2.1 CSV model collections.
- **Boundary:** Does not retrieve missing entities, perform external I/O, or validate the finished dataset against an external SDS service.

### `FileHelper`, `BlobClientFactory`, and `BlobPathHelper`: Blob output

Select Blob authentication, normalize prefixes, create the container, serialize CSV, stage datasets, snapshot live Blobs, promote complete datasets, roll back failed promotion, and manage guardian-file lifecycle. Detailed intended behavior is in the [publication contract](contracts/PUBLICATION.md).

- **Inputs:** Validated storage settings, SDS record sets, Blob prefixes, and cancellation.
- **Side effects:** Container creation, Blob uploads, promotion/rollback, and guardian-file removal required by the run plan.
- **Boundary:** Does not select institutions or locations and does not decide export population.

### Infrastructure and delivery

`infra/main.bicep` is the source for Azure resources. It provisions Blob Storage, Key Vault, Log Analytics, a Container Apps environment, a scheduled Job, and narrow role assignments. The Dockerfile builds the supported non-root Linux image. Workflows validate and release the project.

Infrastructure does not provision Somtoday access, a Microsoft SDS ingestion configuration, or operator privacy governance.

## Current observed execution

1. `Program` creates the generic host, logger, HTTP client factory, `FileHelper`, and `SomtodaySecretProvider`.
2. The secret provider resolves Key Vault or Development credentials.
3. `SyncConfiguration` validates run settings; `SettingsHelper` initializes username expressions separately.
4. `BlobClientFactory` creates a container client and `FileHelper` creates the container if needed.
5. For each configured institution UUID, the process authenticates and selects locations.
6. Locations within an institution and institutions themselves are processed sequentially. Group, employee, pupil, and optional guardian requests run concurrently within one location.
7. V1 and V2 helpers construct in-memory record collections using the same captured Amsterdam date and guardian export policy.
8. `FileHelper` serializes UTF-8 CSV without a byte-order mark and currently uploads each file with overwrite enabled.
9. The process returns `0` only when no institution was recorded as failed; configuration, cancellation, secret, storage, discovery, or recorded institution failures return `1`.

## Current constraints and observations

- At least one unique valid institution UUID, a client ID, and an effective secret are required.
- Outside Development, Key Vault and a Blob service URI are required; Blob connection strings are rejected. In Development, a service URI takes precedence over a connection string.
- Location matching is case-insensitive. Empty inclusion means all locations; exclusion wins over inclusion.
- Institution and location abbreviations must remain non-empty after normalization. Slash and backslash become `_`, and paths are compared case-insensitively.
- Current case-insensitive institution-folder collisions and same-institution location-folder collisions fail the affected institution.
- Normal mode emits both SDS versions for every location aggregate accepted by `OpenAPIHelper`; no configuration switch selects only one version.
- Class identifiers currently combine a normalized group/location-derived name with a school-year suffix based on the run's Amsterdam date and an August boundary.
- Non-storage failure in one institution does not prevent later institutions from being attempted, but any recorded institution failure makes the process exit with `1`.
- Authentication bodies, tokens, secrets, API bodies, and raw exception messages are excluded from application error summaries by current tests.
- Cancellation flows through retries, HTTP, Key Vault, and Blob operations and results in exit code `1`.
- The username formatter accepts every public property on the generated `Medewerker` or `Leerling` model. The types include unsuitable or sensitive fields, and their property sets differ.
- Examples of exposed but unsuitable or sensitive properties include BSN/ECK identifiers, dates, phone numbers, and nested objects.
- Composite username text must currently start with `{user.` and end with `}` or normalization treats the entire value as one property. Tests do not yet define every supported Dynamic LINQ operation, null case, or casing result.

## Known deviations

Use [the deviation register](DEVIATIONS.md) for current gaps. Do not restate deviation details in architecture changes; reference their stable IDs.
