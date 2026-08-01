# Publication contract

This document defines confirmed intended output grouping and Blob publication behavior. Current mismatches are listed only by ID in [the deviation register](../DEVIATIONS.md).

## Institution abbreviation source

The public production endpoint at `https://api.somtoday.nl/rest/v1/connect/instelling` is the authoritative source of `Instelling.Afkorting` for the application and operators, even when synchronization data comes from TEST, ACCEPTATIE, or NIGHTLY. Retrieve this list once per run through a separate unauthenticated client and preserve application cancellation.

Each configured institution UUID must match exactly one public record whose abbreviation is valid for path planning. Use that public record's name and abbreviation for logging and paths. A missing, duplicate, or invalid match fails only that institution. If retrieval of the complete public list fails, abort the run before publishing any SDS dataset.

## Output grouping

Two independent Boolean settings control dataset scope:

- `Output:SeparateByInstitution`, default `true`.
- `Output:SeparateByLocation`, default `false`.

Paths below the configured `Output:Folder` prefix are:

| Per institution | Per location | Path | Dataset scope |
|---|---|---|---|
| `false` | `false` | `v1|v2/{FileName}` | One dataset per SDS version aggregating every configured institution and selected location |
| `true` | `false` | `{InstitutionAfkorting}/v1|v2/{FileName}` | One dataset per institution and SDS version, aggregating selected locations; default |
| `false` | `true` | `{LocationAfkorting}/v1|v2/{FileName}` | One dataset per location abbreviation and SDS version |
| `true` | `true` | `{InstitutionAfkorting}/{LocationAfkorting}/v1|v2/{FileName}` | One dataset per selected location and SDS version under its institution |

Every planned output scope always schedules and attempts both the `v1` and `v2` publication units from the same included locations. The layout settings change only institution/location grouping; they cannot select, disable, or exclude an SDS version.

Slash and backslash characters in abbreviations become `_`. Paths are compared case-insensitively.

An institution folder is named from the matching public `Instelling.Afkorting`; a location folder is named from `Vestiging.Afkorting`.

When institution separation is disabled and equal sanitized location abbreviations occur in different institutions, only the colliding folders are named `{InstitutionAfkorting}_{LocationAfkorting}`. Other unresolved output-path conflicts fail the affected institution.

Plan location folder names from all selected locations before normal-mode population eligibility is evaluated. A location therefore keeps its planned folder name when another selected location is later omitted or its institution fails during data download.

If public matching, institution discovery, output-layout validation, or data download fails for an institution, omit that institution from combined publication scopes, continue with the successfully resolved subset, and fail the run. Publishing a successful subset can remove the failed institution's earlier rows from a combined live dataset.

## Population eligibility and retained output

Normal-mode datasets contain only locations with at least one exportable class under the [export population rules](EXPORT.md#class-and-person-population). When grouping combines multiple locations, ineligible locations are omitted while eligible locations are still published. If no location remains in a publication unit, the application logs a warning and skips the unit without staging, promotion, deletion, or run failure. Existing live output for a skipped unit remains unchanged.

Header-only mode is exempt from normal-mode population eligibility and publishes every selected scope.

## Grouped dataset assembly

Transform all included locations in publication-plan order into one dataset. V1 and V2.1 apply the grouped identity rules in the [export contract](EXPORT.md#grouped-dataset-identities). Exact duplicate relationship, role, roster, and enrollment rows are emitted once.

If different Somtoday classes map case-insensitively to the same SDS class identifier, generation of the affected SDS-version publication unit fails and its existing live output remains unchanged. Do not alter the identifier automatically.

Conversion and upload results are failure-isolated per planned scope and SDS version. A conversion failure blocks that complete publication unit; a conversion or upload failure marks every participating institution as failed and does not prevent the mandatory attempt for the other SDS version or another output scope.

## Publication unit and staging

Each mandatory SDS-version dataset from the table is a separate, failure-isolated publication unit. This separation does not make either version optional.

1. Generate its complete CSV set successfully in memory.
2. Assign the same metadata to every file:
   - `syncidproducer=Somtoday2MicrosoftSDS`
   - `syncidrunutc=<the job's UTC ISO-8601 run timestamp>`
   - `syncidsdsversion=v1|v2`
   - `syncidguardians=true|false`
3. Create one UUIDv7 `RunId` when the application run starts and upload the complete set once to `{LivePrefix}/.staging/{RunId}/` before overwriting live output.
4. Promote each staged file to its known live name with a server-side Blob copy.

Promotion gets one initial attempt plus three complete-set retries. Every retry starts again with the first promotion action; staging is not uploaded again. Use the Azure Blob SDK's default retries and timeouts without an application timeout, delay, or snapshot layer. A successful promotion can temporarily expose old and new files during the same run.

A staging or conversion failure marks the affected dataset and its participating institutions as failed while leaving live output unchanged. It does not suppress the mandatory attempt for another SDS version or later scope. Application cancellation stops immediately without rollback. No success-marker Blob is used.

Delete the dataset's staging files after successful promotion or completed error handling. At startup, remove active application-owned staging Blobs left by an aborted run. Runs must not overlap: startup cleanup is allowed to remove another run's staging data, so overlapping runs are unsupported.

## Complete-set rollback from Blob versions

[Blob versioning](https://learn.microsoft.com/en-us/azure/storage/blobs/versioning-overview) must be enabled for the output storage account. Promotion does not inspect existing live files, metadata, or version IDs. Manually initialized or corrected live files are therefore overwritten normally.

Only after all four promotion attempts fail, list base Blobs and versions for the dataset's known live file names. A version is an eligible rollback source only when all four application metadata values are present and valid. Exclude the current failed run, group older versions by run timestamp, SDS version, and guardian setting, and select the newest older group that is complete. If retries created multiple versions of one file for the same run timestamp, select the version with the newest Blob last-modified timestamp.

A complete group contains:

- V1: `School.csv`, `Section.csv`, `Teacher.csv`, `Student.csv`, `TeacherRoster.csv`, and `StudentEnrollment.csv`, plus `User.csv` and `Guardianrelationship.csv` when that group has guardian sync enabled.
- V2.1: `orgs.csv`, `users.csv`, `roles.csv`, `classes.csv`, and `enrollments.csv`, plus `relationships.csv` when that group has guardian sync enabled.

Restore every file in the chosen group to its live name. When the chosen group has guardian sync disabled, remove the known guardian-specific live files. Unknown files and versions without valid application metadata are never rollback sources and are not automatically removed.

If no complete older application set exists, restore nothing, fail fatally, and do not process later datasets. If an individual restore or guardian-removal action fails, attempt the remaining rollback actions and then fail fatally. After a complete successful rollback, only the affected dataset and its participating institutions are failed; processing continues with later SDS versions and scopes.

## Guardian file lifecycle

When guardian sync is disabled, remove these previously published files from every affected destination:

- V1 `User.csv`
- V1 `Guardianrelationship.csv`
- V2.1 `relationships.csv`

When guardian sync is enabled but produces no guardian or relationship records, publish the guardian-specific files with headers only.

Automatic published-output cleanup is otherwise intentionally limited. Output belonging to renamed, removed, newly excluded, or normal-mode-ineligible institutions and locations is retained when its complete publication unit is skipped.
