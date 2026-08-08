# Architecture and data flow

## Components

- `Program` owns the one-shot run, school-level failure isolation, successful-subset aggregation, selected-format conversion, publication sequencing, and final exit code.
- `SyncConfiguration` validates one SDS source name, one or more unique Somtoday institution UUIDs, credentials, location filters, username rules, guardian mode, and header-only mode.
- `OpenAPIHelper` owns Somtoday authentication and reads. It uses separate no-redirect clients for authentication, public discovery, and authenticated data, and it preserves cancellation and the existing bounded authentication retries.
- `ExportPopulationResolver`, `SDScsvHelperV1`, and `SDScsvHelperV2` own population and field mapping. They have no network or Azure side effects.
- `FileHelper` constructs a complete in-memory CSV set in UTF-8 without BOM before publication starts.
- `SdsGraphClient` owns connector resolution, Graph authentication, upload-session creation, unauthenticated Azure Data Lake Storage Gen2 file create, append, and flush requests, validation start, and validation polling.
- `HttpRetryPolicy` owns the shared four-attempt transient HTTP policy.

## Run sequence

1. Validate configuration and username expressions.
2. Authenticate to Graph with `DefaultAzureCredential` and resolve the connector with `SourceName`.
3. Select V1 or V2.1 from the connector's `fileFormat.code`.
4. Discover every configured Somtoday institution independently.
5. Download and resolve all successful institutions, or build the selected header-only set.
6. Construct one complete in-memory dataset from the successful subset.
7. Request a fresh upload session with `resetSession=true`.
8. Create, append, and flush every file sequentially in the returned Azure Data Lake Storage Gen2 SAS container without a Graph bearer token.
9. Start connector validation and poll the returned Graph operation every five seconds.
10. Exit `0` only when SDS succeeds and every configured school succeeded; otherwise exit `1`.

No permanent application-owned output store exists. The only CSV persistence is the temporary SDS-owned SAS container. `infra/main.bicep` creates one Container Apps Environment, its Log Analytics Workspace, and the selected resource group's `Somtoday2MicrosoftSDS.environment` tag. `infra/deploy-sync-job.bicep` reads that tag from the same resource group and creates one Job. `infra/assign-sync-job-roles.ps1` assigns the required Microsoft Graph application roles to all tagged Job identities in tagged resource groups. Each Job has one SDS source name and its own system-assigned identity.

Detailed data and failure rules are authoritative in the [export](contracts/EXPORT.md) and [publication](contracts/PUBLICATION.md) contracts.
