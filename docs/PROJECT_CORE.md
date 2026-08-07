# Project core

## Status and authority

This document is the mandatory starting point for every contribution. Source precedence is:

1. This core defines project-wide scope and invariants.
2. Focused contracts under `docs/contracts/` define detailed intended behavior.
3. `docs/ARCHITECTURE.md` defines intended component boundaries and current execution structure.
4. `docs/DEVIATIONS.md` records known differences between intent and implementation.
5. Operator and development guides describe procedures and do not override contracts.
6. Code and tests are evidence of current behavior, not automatic proof of intended behavior.

Use these labels when provenance matters: **Confirmed intent**, **Code-observed fact**, **External constraint**, **Existing documentation claim**, **Inference**, **Discrepancy**, and **Implementation deviation**. Do not silently resolve an unexplained conflict between sources.

## Purpose and scope

Somtoday2MicrosoftSDS is a one-shot .NET 10 batch application. One run authenticates to one or more configured Somtoday institutions, downloads the current school-year data for selected locations, converts the successful sources into one complete Microsoft School Data Sync (SDS) V1 or V2.1 CSV dataset, uploads that dataset directly to the temporary SAS container returned by Microsoft Graph, starts SDS validation, waits for its terminal result, and exits.

The only supported production deployment is a scheduled Azure Container Apps Job. Each Job has one SDS inbound-flow ID, one or more Somtoday institution UUIDs, its own Somtoday credentials and configuration, and its own system-assigned managed identity. Multiple Jobs can share one Container Apps Environment. Local execution is supported for development and testing.

Confirmed intended scope includes multiple Somtoday institutions per Job, location selection, configurable teacher and pupil username rules, optional guardian relationships, header-only output, direct Microsoft SDS upload, and validation polling.

## Terminology

- A **Somtoday institution** is one configured Somtoday instance identified by an institution UUID.
- An **inbound flow** is the configured Microsoft SDS ingestion activity identified by `InboundFlowId`.
- A **connector** is the Graph `azureDataLakeConnector` related to that inbound flow. Its `ConnectorId` and `InboundFlowId` are separate identifiers.
- A **dataset** is the complete file set for exactly one SDS version. The connector's `fileFormat.code` selects V1 or V2.1.
- An **upload session** is the short-lived Graph response containing the SDS-owned SAS container used only by the current dataset upload.

## Non-goals and boundaries

- No Windows production deployment, HTTP service, interactive application, durable queue, or continuously running scheduler is supported.
- The project does not provision Somtoday access or create/configure the SDS inbound flow or connector.
- The project creates no application registration, client secret, user-assigned identity, permanent output Storage Account, Power Automate flow, staging store, promotion path, rollback store, Blob versioning, or lifecycle policy.
- The project does not replace operator privacy governance, access reviews, retention policy, or AVG/GDPR assessment.

## Key invariants

- Each run resolves the connector through `/external/industryData/inboundFlows/{InboundFlowId}/dataConnector`; `ConnectorId` is never configurable.
- `schoolDataSyncV1` selects one complete V1 dataset and `schoolDataSyncV2Rev1` selects one complete V2.1 dataset. No run uploads both formats.
- Successful Somtoday institutions are combined into the dataset. A failed institution is omitted, the successful subset remains uploadable, and the final process exit code is nonzero.
- V1 and V2.1 use the same population rules. A class requires at least one resolved exportable teacher and pupil; people without an included class are excluded.
- Normal mode skips upload when no selected location from any successful institution contains an exportable class. Header-only mode produces the selected format's complete header-only set.
- Dataset construction completes before the upload session is requested. Files upload sequentially; any failed upload prevents validation.
- Every dataset requests a new upload session with `resetSession=true`, uploads only to its returned temporary SAS container, then starts and polls connector validation.
- Network retries are bounded, cancellation is preserved, `Retry-After` is respected, permanent 4xx responses are not retried, and unknown validation statuses are never treated as success.
- Somtoday authentication, public discovery, and authenticated data requests do not follow redirects. Every 3xx response is a permanent protocol failure for that operation.
- Production Graph authentication uses `DefaultAzureCredential` constrained to the Container Apps Job's system-assigned managed identity.
- Secrets, tokens, SAS URLs/querystrings, authentication bodies, personal data, production CSV data, and unsafe exception detail must not enter tracked files or logs.

## Task-based routing matrix

| Task | Required additional reading | Read README? |
|---|---|---|
| Export population, mappings, usernames, guardians, header-only behavior | `contracts/EXPORT.md`, `DEVIATIONS.md` | If externally visible |
| Graph connector, upload session, SAS upload, retries, validation | `contracts/PUBLICATION.md`, `ARCHITECTURE.md`, `SECURITY.md`, `DEVIATIONS.md` | Yes |
| Configuration | `operations/CONFIGURATION.md`, relevant contract, `DEVIATIONS.md` | Yes |
| Azure resources, identity, scheduling, deployment | `operations/DEPLOYMENT.md`, `ARCHITECTURE.md`, `SECURITY.md` | Yes |
| Releases or dependencies | `operations/RELEASES.md`, `THIRD-PARTY-NOTICES.md`, `NOTICE.md`, `DEVELOPMENT.md` | If externally visible |
| Local build, tests, container smoke tests, generated client | `DEVELOPMENT.md`, `CONTRIBUTING.md` | No |
| Documentation-only change | Documents being changed and their referenced sources | When changing README |

## Documentation maintenance

- Update this core only when project-wide purpose, scope, non-goals, invariants, authority, or routing changes.
- Update the relevant contract when intended behavior changes and architecture when ownership, data flow, or side-effect boundaries change.
- Add or update a stable deviation ID when implementation and confirmed intent differ. Resolved IDs remain historical and are never reused.
- Keep `README.md` in Dutch. All other repository documentation and contributor instructions are English.
- In normative text, interpret RFC 2119 key words using RFC 8174 semantics (only when the words are in all capitals).
- Prefer ASD-STE100 Simplified Technical English for technical and contributor documentation where practical.
