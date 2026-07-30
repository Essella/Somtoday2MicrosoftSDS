# Publication contract

This document defines confirmed intended output grouping and Blob publication behavior. Current mismatches are listed only by ID in [the deviation register](../DEVIATIONS.md).

## Institution abbreviation source

The public production endpoint at `https://api.somtoday.nl/rest/v1/connect/instelling` is the authoritative source of `Instelling.Afkorting` for the application and operators, even when synchronization data comes from TEST, ACCEPTATIE, or NIGHTLY.

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

Slash and backslash characters in abbreviations become `_`. Paths are compared case-insensitively.

An institution folder is named from the matching public `Instelling.Afkorting`; a location folder is named from `Vestiging.Afkorting`.

When institution separation is disabled and equal sanitized location abbreviations occur in different institutions, only the colliding folders are named `{InstitutionAfkorting}_{LocationAfkorting}`. Other unresolved output-path conflicts fail the affected institution.

## Publication unit and staging

Each SDS-version dataset from the table is an independent publication unit.

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

Automatic published-output cleanup is otherwise intentionally limited. Output belonging to renamed, removed, or newly excluded institutions and locations is retained.

## Current implementation references

See `DEV-003`, `DEV-004`, `DEV-006`, and `DEV-007` in [the deviation register](../DEVIATIONS.md).
