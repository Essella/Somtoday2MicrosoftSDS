# Project core

## Status and authority

This concise document is the mandatory starting point for every contribution. It defines project purpose, scope, non-goals, key invariants, source-of-truth rules, and documentation routing.

Use this precedence when sources differ:

1. This core defines project-wide scope and invariants.
2. The focused documents under `docs/contracts/` define detailed intended behavior.
3. `docs/ARCHITECTURE.md` defines intended component boundaries and separately records current execution structure.
4. `docs/DEVIATIONS.md` is the authoritative register of known differences between intended and current behavior.
5. Operator and development guides describe procedures; they do not override the core or contracts.
6. Code and tests are evidence of current behavior, not automatic proof of intended behavior.

Do not silently choose a winner when code, tests, and authoritative documentation conflict. Report the conflict and request clarification unless the intended behavior is already identified by a deviation ID.

Use these evidence labels when provenance matters:

| Label | Meaning |
|---|---|
| **Confirmed intent** | Explicitly confirmed by the project owner and normative for future implementation and documentation |
| **Code-observed fact** | Directly supported by current code or tests; not automatically intended behavior |
| **External constraint** | Required by an identified external specification or platform |
| **Existing documentation claim** | Present in repository documentation but not confirmed as project intent |
| **Inference** | A plausible interpretation that is not a requirement or design decision |
| **Discrepancy** | Sources describe different behavior and no authoritative resolution is recorded |
| **Implementation deviation** | Current code, tests, automation, or infrastructure do not satisfy confirmed intent |

Focused documents apply a label to an entire section where possible instead of repeating it on every sentence. Anything unconfirmed must remain explicitly labelled and non-normative.

## Purpose and scope

Somtoday2MicrosoftSDS is a one-shot .NET 10 batch application. A run authenticates to one or more configured Somtoday institutions, downloads current school-year data for selected locations, converts it to Microsoft School Data Sync (SDS) V1 and V2.1 CSV datasets, publishes the datasets to Azure Blob Storage, and exits.

The only supported production deployment is a scheduled Azure Container Apps Job. Intended operators are school administrators and Azure partners acting for schools. Local execution is supported for development and testing.

Confirmed intended scope includes multiple Somtoday institutions, location selection, configurable output grouping, configurable teacher and pupil username rules, optional guardian relationships, and header-only output.

## Terminology

A **Somtoday institution** is one configured Somtoday instance identified by an institution UUID and represented by an `Instelling` record. A **dataset** is one complete output set for one SDS version and one scope selected by the output-layout settings.

## Non-goals and boundaries

- No Windows executable or other Windows production deployment is supported.
- The application is not an HTTP service, interactive UI, durable queue, or continuously running scheduler.
- The project does not provision Somtoday access or a Microsoft SDS ingestion configuration.
- The project does not replace operator privacy governance, access reviews, retention policy, or AVG/GDPR assessment.
- Automatic cleanup of published output is limited to guardian-specific files; renamed, removed, or newly excluded institution/location output is retained.

## Key invariants

The following invariants are confirmed intent:

- Every eligible normal-mode output scope always plans both SDS V1 and V2.1 from the same included population; neither format is legacy and neither can be disabled or excluded by configuration or command-line selection. Each SDS version is a separate publication unit only for failure isolation, so failure of one version does not suppress the attempt for the other version or another output scope.
- V1 and V2.1 use the same population rules. A class requires at least one resolved exportable teacher and one resolved exportable pupil. People without an included class are excluded from all exported files.
- Normal mode includes only locations with at least one exportable class. A publication scope with no included location is skipped with a warning and without changing existing output. Header-only mode still produces valid header-only files for every selected scope.
- Each mandatory SDS-version dataset is a separate, failure-isolated publication unit. Its complete file set is generated and staged before live output is overwritten.
- Publication uploads each complete in-memory dataset once to staging and then uses one promotion attempt plus three complete-set retries with the Azure Blob SDK's default retry and timeout behavior. After exhausted promotion, it restores the newest older complete application-authored set from Blob versions; absence or failure of that rollback stops the whole application.
- Institution and location output grouping is controlled independently; the default is separation by institution but not by location.
- School-year boundaries and the automatic July 31 header-only trigger use `Europe/Amsterdam`, including CET/CEST.
- Guardian output follows the confirmed consent, name, email, phone, and secret-number rules in the export contract.
- Secrets, tokens, authentication bodies, personal data, production CSV data, and unsafe exception detail must not enter tracked files or logs.

## Task-based routing matrix

Always read this core. Then read only the documents selected by this matrix.

| Task | Required additional reading | Read operator-facing README? |
|---|---|---|
| Export population, SDS mapping, usernames, guardians, header-only behavior | [Export contract](contracts/EXPORT.md), [deviation register](DEVIATIONS.md) | Only if externally visible behavior changes |
| Blob paths, grouping, staging, retries, rollback, output cleanup | [Publication contract](contracts/PUBLICATION.md), [deviation register](DEVIATIONS.md) | Only if externally visible behavior changes |
| Component ownership, orchestration, data flow, side effects | [Architecture](ARCHITECTURE.md), [deviation register](DEVIATIONS.md) | No, unless behavior becomes externally visible |
| Configuration keys or username expressions | [Configuration guide](operations/CONFIGURATION.md), relevant contract, [deviation register](DEVIATIONS.md) | Yes |
| Azure resources, identity, Key Vault, scheduling, deployment | [Deployment guide](operations/DEPLOYMENT.md), [architecture](ARCHITECTURE.md), [security policy](../SECURITY.md) | Yes |
| Releases, artifacts, versioning, CI release behavior | [Release guide](operations/RELEASES.md), [deviation register](DEVIATIONS.md), [notice](../NOTICE.md) | Yes |
| Local build, tests, container smoke tests, generated client | [Development guide](DEVELOPMENT.md), [contributing guide](../CONTRIBUTING.md) | No |
| Security, privacy, secrets, logging | [Security policy](../SECURITY.md), relevant contract/operations guide | Only if operator action or public behavior changes |
| Dependency or licensing changes | [Release inventory policy](operations/RELEASES.md#dependency-inventory-authority), [third-party notices](../THIRD-PARTY-NOTICES.md), [notice](../NOTICE.md), [development guide](DEVELOPMENT.md) | Only if operator/release information changes |
| Documentation-only change | The documents being changed and any source documents they reference | Only when changing README or operator-visible guidance |

## Documentation maintenance

- Update this core only when project-wide purpose, scope, non-goals, invariants, authority, or routing changes.
- Update the relevant contract when intended behavior changes.
- Update `docs/ARCHITECTURE.md` when component responsibilities, data flow, or side-effect boundaries change.
- Add or update a stable deviation ID when implementation and confirmed intent differ; do not duplicate its current-behavior description elsewhere.
- Update operator-facing documentation only for configuration, deployment, release, usage, or other externally visible changes.
- Keep `README.md` in Dutch. All other repository documentation and contributor instructions are written in English.
