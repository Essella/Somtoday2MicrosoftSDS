# Project context

## Document status and evidence labels

This document is the leading source of truth for confirmed project intent. It was created during a repository audit on 2026-07-30 and deliberately separates intended behavior from observable implementation.

Repository documentation and contributor instructions are written in English, with one deliberate operator-facing exception: `README.md` is maintained in Dutch.

The following labels are used throughout this document:

- **Code-observed fact:** directly supported by the current implementation or tests.
- **Confirmed intent:** explicitly confirmed by the project owner and normative for future implementation and documentation.
- **Existing documentation claim:** stated by an existing repository document but not yet confirmed by the project owner during this audit.
- **Inference:** a plausible interpretation that must not be treated as a requirement or design decision.
- **Discrepancy:** sources describe or imply different behavior.
- **Implementation deviation:** current code, tests, delivery automation, or infrastructure do not satisfy confirmed intent.

## Purpose, operators, and supported runtime

- **Code-observed fact:** The solution contains one .NET 10 executable project and one xUnit test project.
- **Code-observed fact:** Each process invocation performs one synchronization run and then exits. There is no HTTP server, interactive UI, durable queue, or continuously running scheduler in the application.
- **Code-observed fact:** A run reads one or more configured Somtoday institution UUIDs, authenticates separately for each institution, selects locations, downloads current-school-year groups and people, converts the data to Microsoft School Data Sync (SDS) V1 and V2.1-shaped CSV records, and uploads those CSV files to Azure Blob Storage.
- **Confirmed intent:** The only supported deployment method is an Azure Container Apps scheduled Job. Local execution is a development and test facility, not a supported production deployment.
- **Confirmed intent:** The intended operators are school administrators and Azure partners acting for schools.
- **Confirmed intent:** There is no supported Windows executable or Windows deployment artifact.
- **Confirmed terminology:** A **Somtoday institution** is one Somtoday instance identified by a configured institution UUID and represented by an `Instelling` record. This term is used instead of the ambiguous unqualified term “school” when referring to that configuration and isolation boundary.
- **Code-observed fact:** The application can optionally include guardians and guardian relationships. This adds personal data to the output.
- **Implementation deviation:** The current release workflow still creates and publishes a framework-dependent Windows archive. That delivery behavior must be removed in a separately authorized implementation change.

## Current execution and data flow

1. `Program` creates the generic host, logging, HTTP client factory, `FileHelper`, and `SomtodaySecretProvider`.
2. `SomtodaySecretProvider` resolves the effective Somtoday client secret from Azure Key Vault, or directly from Development configuration when no vault is configured.
3. `SyncConfiguration` validates and normalizes run configuration. `SettingsHelper` initializes and validates username expressions separately.
4. `BlobClientFactory` creates the target container client. `FileHelper` creates the container when it does not exist.
5. For each configured institution UUID, `Program` creates an `OpenAPIHelper`, authenticates, selects the matching institution, and selects its locations.
6. Institution and location abbreviations become Blob path segments after limited sanitization. Case-insensitive path collisions make the affected institution fail.
7. For each selected location, `OpenAPIHelper` retrieves groups, employees, pupils, and optionally guardians. The four requests run concurrently within a location; locations and institutions are otherwise processed sequentially.
8. `SDScsvHelperV1` and `SDScsvHelperV2` construct in-memory SDS records from the downloaded location model.
9. `FileHelper` serializes each dataset as UTF-8 CSV without a byte-order mark and uploads it with overwrite enabled.
10. The process returns exit code `0` only when no institution was recorded as failed; configuration, cancellation, secret, storage, discovery, or recorded institution failures return `1`.

## Intended output layout

Two intended Boolean layout options independently control separation by Somtoday institution and by location:

- `Output:SeparateByInstitution` / `Output__SeparateByInstitution`, default `true`.
- `Output:SeparateByLocation` / `Output__SeparateByLocation`, default `false`.

| Institution separation | Location separation | Path below `OutputPrefix` | Dataset scope |
|---|---|---|---|
| false | false | `v1|v2/{FileName}` | All configured institutions and selected locations are aggregated into one dataset per SDS version. |
| true | false | `{InstitutionAfkorting}/v1|v2/{FileName}` | One dataset per Somtoday institution, aggregating its selected locations. |
| false | true | `{LocationAfkorting}/v1|v2/{FileName}` | One dataset per location abbreviation without an institution parent folder. A collision between institutions is disambiguated as `{InstitutionAfkorting}_{LocationAfkorting}` for each colliding folder. |
| true | true | `{InstitutionAfkorting}/{LocationAfkorting}/v1|v2/{FileName}` | One dataset per selected location beneath its Somtoday institution. |

- **Confirmed intent:** The public production endpoint at `https://api.somtoday.nl/rest/v1/connect/instelling` is the authoritative source of `Instelling.Afkorting` for both the application and operators, including when the data synchronization itself uses another configured Somtoday environment.
- **Confirmed intent:** The same selected layout applies to both required SDS versions.
- **Confirmed intent:** A location-folder collision across Somtoday institutions does not fail when institution separation is disabled. Only the colliding folders are renamed to `{SanitizedInstitutionAfkorting}_{SanitizedLocationAfkorting}`.
- **Current behavior:** The application hard-codes the final layout in the last row of the table and has no layout configuration options.

## Component responsibilities

The project owner confirmed that the current high-level distribution of responsibilities is the intended architecture. These boundaries are normative: changing this document requires a corresponding project change, and changing the project requires this document to be updated.

### Process orchestration: `Program`

- **Intended responsibility:** Owns the run lifecycle, composition root, cross-institution error isolation, retry loop, path-collision checks, mode selection, conversion sequencing, upload sequencing, and final exit code.
- **Inputs:** Command-line arguments, host configuration, current date, dependency-injection services, and application cancellation.
- **Outputs and side effects:** Logs, Somtoday requests, Key Vault access through the secret provider, Blob writes through the file helper, and a process exit code.
- **Boundary:** It does not implement field-level SDS mapping or raw API transport.

### Validated run settings: `SyncConfiguration` and `SettingsHelper`

- **Intended responsibility:** `SyncConfiguration` builds an immutable run configuration and validates required Somtoday and Blob settings. `SettingsHelper` separately stores and evaluates process-global username expressions.
- **Inputs:** Layered .NET configuration and the already-resolved client secret.
- **Outputs:** A `SyncConfiguration` value, validation errors, and compiled username accessors.
- **Boundary:** These components do not retrieve secrets or test external connectivity.
- **Relevant side effect:** `SettingsHelper.Initialize` changes static process-wide formatter state.
- **Confirmed intent:** Teacher and pupil username settings are intentionally configurable expression rules, not merely direct field selectors. A school administrator uses them to reproduce the school's IDM username or email-generation rules when the IDM-generated value differs from the value stored in Somtoday. The current ability to combine user properties, literal text, and Dynamic LINQ operations is therefore part of the intended configuration surface.
- **Code-observed username behavior:** A bare configured value is normalized to a direct property expression. For example, `Emailadres` becomes `{user.Emailadres}`. Other common direct values include `Gebruikersnaam`, teacher `Medewerkernummer`, and pupil `Leerlingnummer`.
- **Code-observed username behavior:** A value that already starts with `{user.` and ends with `}` is parsed as one or more Dynamic LINQ expressions with literal segments between braces. `{user.Voorletters}.{user.Achternaam}` is a composite-property example. `{user.Emailadres.ToLower()}` is a method-call example. The parser can also attempt concatenation expressions such as `{user.Voorletters + "." + user.Achternaam + "@school.example"}`.
- **Code-observed limitation:** The current formatter exposes every public property available on the generated `Medewerker` or `Leerling` type, not an approved allowlist. Those types include sensitive or unsuitable values such as BSN/ECK identifiers, dates, phone numbers, and nested objects. Teacher and pupil property sets also differ.
- **Code-observed limitation:** Composite formatting is brittle: the complete configured string must start with `{user.` and end with `}` or the normalizer wraps it as if it were a single property. There are no tests defining supported Dynamic LINQ syntax, safe properties, null behavior, or stable output casing.

### Secret lifecycle: `SomtodaySecretProvider`

- **Intended responsibility:** Resolves the effective client secret and, when a bootstrap value is supplied, creates or versions a Key Vault secret.
- **Inputs:** Vault URI, secret name, temporary bootstrap value, Development secret, environment mode, and cancellation.
- **Outputs and side effects:** Returns a secret; may read or write Azure Key Vault; logs only lifecycle status, not secret values.
- **Boundary:** It does not rotate the upstream Somtoday credential or delete old Key Vault versions.
- **Failure behavior:** Missing required values, invalid Vault settings, authorization failures, Vault service failures, and failed writes stop the entire run.

### Somtoday adapter: `OpenAPIHelper` and generated client

- **Intended responsibility:** Performs OAuth client-credentials authentication for one institution, configures the generated client, pages through current-school-year datasets, filters locations, and assembles one aggregate model for every selected location, even when one or more datasets are empty.
- **Inputs:** Credentials, institution UUID, environment endpoints, selected locations, guardian flag, and cancellation.
- **Outputs and side effects:** Somtoday HTTP requests and in-memory `VestigingModel` values.
- **Boundary:** It does not write CSV or Blob data.
- **Confirmed provenance:** The project owner generated `OpenAPIs/openapi.cs` with Visual Studio from the tracked OpenAPI 3.0.3 specification identified as version 14.10.0. No additional redistribution confirmation is required for release.

### SDS transformation: `SDScsvHelperV1`, `SDScsvHelperV2`, and CSV models

- **Intended responsibility:** Maps one location aggregate to separate SDS V1 and V2.1 record sets, including identifiers, roles, enrollments, usernames, and optional guardian relationships.
- **Inputs:** A `VestigingModel` and process-global username formatters.
- **Outputs:** In-memory CSV model collections; no direct external I/O.
- **Boundary:** The transformers do not retrieve missing entities and do not validate the finished dataset against an external SDS schema.
- **Confirmed intent:** SDS V1 and V2.1 must apply the same population rules. Teachers and pupils without an included class must be absent from every exported file, and empty classes must be absent from every exported file.
- **Confirmed intent:** A class is exportable only when at least one resolved, exportable teacher and at least one resolved, exportable pupil remain after filtering. Teacher-only and pupil-only classes are excluded.
- **Confirmed intent:** Both SDS V1 and V2.1 are relevant, non-legacy outputs and must be generated on every normal run.
- **Confirmed intent:** A selected location must still produce valid files containing all exportable data that is available when one or more source datasets are empty.
- **Confirmed guardian mapping:** SDS V1 `First Name` and V2.1 `givenName` use `OuderVerzorger.Voorletters`. SDS V1 `Last Name` and V2.1 `familyName` join the non-empty `Voorvoegsel` and `Achternaam` with one space. `Email` uses `Emailadres`. `Phone` uses `Telefoonnummer` after E.164 normalization.
- **Confirmed privacy rule:** When `WenstContactViaEMail` is false, the complete guardian and all its relationships are omitted because a valid SDS guardian relationship requires email. A phone value is omitted when Somtoday marks the selected number as secret through its corresponding secret-number flag.
- **Confirmed guardian phone selection:** Determine the source of fallback `OuderVerzorger.Telefoonnummer` by comparing it with the explicit home, mobile, and work values, and apply the flag associated with that source. Suppressed numbers are not candidates. When multiple eligible numbers exist, the preference order is mobile, home, then work.
- **Code-observed rule:** A class is currently considered for emission only when its source group has at least one teacher reference and at least one pupil reference. References to people absent from the downloaded people lists are skipped.
- **Code-observed rule:** Class identifiers combine a normalized group/location-derived name with a school-year suffix based on the local system date and an August boundary.

### Blob output: `FileHelper`, `BlobClientFactory`, and `BlobPathHelper`

- **Intended responsibility:** Selects managed-identity or Development connection-string authentication, normalizes Blob prefixes, creates the container, serializes CSV data, writes a complete batch to a staging folder, snapshots existing destination blobs, promotes a successful staged batch, rolls back a failed promotion, and manages guardian-file cleanup or header-only replacement.
- **Inputs:** Validated storage settings, SDS record sets, Blob prefixes, and cancellation.
- **Outputs and side effects:** Azure Blob container creation, Blob uploads, and removal of guardian-specific blobs when required by the run plan.
- **Boundary:** These components do not decide which schools or locations to process.
- **Confirmed output boundary:** Folder nesting is controlled by the two layout options in the intended output-layout matrix. An institution folder represents its institution UUID but is named from the public production endpoint's matching `Instelling.Afkorting`. A location folder is named from `Vestiging.Afkorting`.
- **Code-observed path shape:** `{OutputPrefix}/{SanitizedInstitutionAbbreviation}/{SanitizedLocationAbbreviation}/v1|v2/{FileName}`.

### Deployment and delivery

- **Intended responsibility:** `infra/main.bicep` provisions Blob Storage, Key Vault, Log Analytics, a Container Apps environment, a scheduled job, and narrowly scoped role assignments. The Dockerfile builds the supported non-root Linux image. GitHub workflows test and gate releases.
- **Boundary:** The infrastructure does not provision Somtoday access, a Microsoft SDS ingestion configuration, privacy governance, or downstream cleanup outside the application-owned output rules.
- **Confirmed repository history:** The project owner confirms that this is a new repository and that no secret has ever been committed. A full-history secret scan is not a release prerequisite.
- **Implementation deviation:** The release workflow still requires `OPENAPI_REDISTRIBUTION_CONFIRMED=true`, although no such approval is required. The obsolete gate must be removed in a separately authorized workflow change.

## Code-observed invariants and constraints

- At least one unique, valid institution UUID, a client ID, and a resolved secret are required.
- Outside Development, Key Vault and a Blob service URI are required; a Blob connection string is rejected. A service URI takes precedence in Development when both storage mechanisms are configured.
- Location matching is case-insensitive. An empty include list means all locations; exclusions take precedence over inclusions.
- Institution and location abbreviations must be non-empty after normalization. Slash and backslash become `_`; path collisions are compared case-insensitively.
- Normal mode currently produces both V1 and V2 output for every location aggregate accepted by `OpenAPIHelper`; there is no configuration switch for only one format.
- Header-only mode is enabled by configuration, `--empty-csv`, or the process-local calendar date July 31.
- **Confirmed intent:** School-year boundaries and the automatic July 31 trigger use Europe/Amsterdam local time, including the applicable CET or CEST offset.
- **Confirmed intent:** The complete file set for each independent SDS-version dataset must be generated successfully in memory before any existing output file for that dataset is overwritten.
- **Confirmed intent:** Publication uses a staging folder. Each resulting SDS-version dataset is an independent publication unit. All files in that dataset are saved to staging before promotion to the real destination. Each dataset receives at most three total attempts, and each attempt has a two-minute timeout. After exhaustion, processing continues to the next dataset.
- **Confirmed publication consistency:** Before promotion, the application creates Blob snapshots of every existing destination file and records which destination files do not yet exist. A successful promotion may temporarily expose a mixture of old and new files. A staging failure leaves the destination untouched. If promotion still fails after three attempts, the application restores every snapshot and deletes every destination file created by that promotion. Rollback must finish before processing continues; a partially updated destination is prohibited. No success marker is used. If a Blob outage also prevents rollback, the entire application stops and no later datasets are processed. The next run neither resumes rollback nor reuses staged data; it downloads current Somtoday data and generates each full dataset again.
- **Confirmed intent:** Disabling guardian sync must remove previously generated V1 `User.csv` and `Guardianrelationship.csv` files and V2 `relationships.csv` files from the affected output paths.
- **Confirmed intent:** When guardian sync is enabled but the generated guardian or relationship collections are empty, all guardian-specific files are still published with headers and no data rows.
- **Confirmed non-goal:** Automatic stale-output cleanup is limited to guardian files. Output for renamed, removed, or newly excluded institutions and locations is retained.
- Non-storage failures in one institution do not prevent later institutions from being attempted, but any recorded institution failure makes the process exit with `1`. A Blob outage that prevents publication rollback stops the entire application immediately.
- Cancellation is propagated through retries, HTTP calls, Key Vault access, and Blob uploads and results in exit code `1`.
- Authentication response bodies, access tokens, secrets, API response bodies, and exception messages are intentionally excluded from application error summaries by current tests.
- **Confirmed configuration rule:** Full environment names (`PROD`, `TEST`, `ACCEPTATIE`, and `NIGHTLY`) are preferred because they are clearest to operators, but they are not required. Accepting any non-empty value whose first trimmed character identifies one of those environments is intentional and reduces configuration failure from minor spelling mistakes.

## Implementation deviations and resolved discrepancies

- **Resolved documentation discrepancy:** Earlier README wording implied that only exact environment names were accepted. The current first-character parsing is intended; the README now describes full names as preferred rather than required.
- **Implementation deviation:** `OpenAPIHelper` silently omits a selected location unless groups, employees, and pupils are all non-empty; guardian-enabled runs also require at least one guardian. Intended behavior requires valid output containing whatever exportable data is available for every selected location.
- **Implementation deviation:** Individual Blob uploads overwrite immediately instead of waiting until the complete run has been generated successfully in memory. A later failure can therefore leave a mixture of new and previous files while the run exits with `1`.
- **Implementation deviation:** The application does not use a staging folder, destination snapshots, a promotion step, or the required publication retry and rollback policy. Current direct uploads can leave a partially updated destination, which the confirmed contract prohibits.
- **Implementation deviation:** Guardian files are not removed when guardian sync is disabled. When guardian sync remains enabled but a collection is empty, normal mode skips the corresponding guardian file instead of publishing a header-only file.
- **Implementation deviation:** V1 exports only teachers and pupils referenced by emitted classes, while V2 exports every downloaded employee and pupil as a user/role, even when not enrolled in an emitted class. Intended behavior requires the V1 population rule in both formats.
- **Implementation deviation:** Both converters test whether source groups contain teacher and pupil references before resolving those references against the downloaded people. A class can therefore be emitted without both an exported teacher and an exported pupil. Intended class eligibility is based on the resolved export population.
- **Confirmed limitation:** Blobs for renamed, removed, or excluded institutions and locations are deliberately not removed.
- **Confirmed guardian rule:** Guardian name components and E.164 phone normalization are confirmed. A guardian is omitted completely when email contact is disallowed. Secret phone numbers are omitted while the guardian and its relationships may otherwise remain.
- **Implementation deviation:** School-year identifiers and automatic July 31 behavior currently use the process-local clock. The intended time basis is Europe/Amsterdam, while the supplied Linux container normally uses UTC unless explicitly configured otherwise.
- **External SDS constraint:** [SDS V1 `user.csv`](https://learn.microsoft.com/en-us/schooldatasync/sds-v1-csv-file-format) uses separate required `First Name` and `Last Name` fields, plus required `Email` and optional E.164 `Phone`. [SDS V2.1 `users.csv`](https://learn.microsoft.com/en-us/schooldatasync/sds-v2.1-csv-file-format) uses separate `givenName` and `familyName`; Microsoft requires both names and `email` for users referenced by a contact relationship. The tracked CSV models contain these columns.
- **Implementation deviation:** V2 guardian users currently populate only `sourcedId`, `username`, and `phone`; `givenName`, `familyName`, and `email` remain empty despite the contact-relationship requirements. V1 populates separate columns but currently places a non-empty prefix plus surname in `First Name` and omits the prefix from `Last Name`, instead of using the confirmed mapping.

## Validation performed during this audit

- Inspected the solution and project files, application startup, configuration, dependency injection, helpers, CSV models, tests, Dockerfile, Bicep template, scripts, workflows, existing Markdown files, generated-client header, OpenAPI metadata, and recent Git history.
- Did not inspect every generated line of `openapi.cs`, every schema entry in `openapi.json`, or the generated ARM JSON because their source counterparts and relevant metadata were sufficient for this phase.
- Ran `dotnet test .\Somtoday2MicrosoftSDS.sln --configuration Release`: 38 tests passed, 0 failed, 0 skipped.
- Read the public Somtoday institution endpoint and official Microsoft documentation. Did not access an authenticated Somtoday environment, Azure subscription, production Blob data, or Microsoft SDS tenant.
