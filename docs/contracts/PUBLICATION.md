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
2. Upload the complete set to staging before overwriting live output.
3. Before promotion, snapshot every existing destination Blob and record planned destination Blobs that do not yet exist.
4. Promote the staged files to the live destination.

Each dataset receives at most three total publication attempts. One attempt covers staging and, when staging succeeds, promotion. Each attempt has a two-minute timeout. A successful promotion may temporarily expose old and new files during the same run.

If staging exhausts its attempts, live output remains untouched and processing continues with the next dataset. If promotion exhausts its attempts, restore every snapshot and delete every Blob created by that promotion before continuing. No success-marker Blob is used.

If a Blob outage also prevents rollback, the entire application stops and no later datasets are processed. A later run does not resume rollback or reuse staging; it downloads current Somtoday data and generates each dataset again.

Delete an attempt's staging data as soon as it is no longer needed. This includes partial staging after a failed attempt, staging after successful promotion, and staging after a completed rollback. Retries and later runs never reuse staging data.

## Guardian file lifecycle

When guardian sync is disabled, remove these previously published files from every affected destination:

- V1 `User.csv`
- V1 `Guardianrelationship.csv`
- V2.1 `relationships.csv`

When guardian sync is enabled but produces no guardian or relationship records, publish the guardian-specific files with headers only.

Automatic published-output cleanup is otherwise intentionally limited. Output belonging to renamed, removed, newly excluded, or normal-mode-ineligible institutions and locations is retained when its complete publication unit is skipped.

## Current implementation references

See `DEV-006` and `DEV-007` in [the deviation register](../DEVIATIONS.md).
