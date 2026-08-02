# Azure deployment guide

## Supported production deployment

The only supported production deployment is a scheduled Azure Container Apps Job using the Linux container image. Local execution is for development and testing; a Windows executable is not supported.

Operators need an Azure subscription, permission to create resources and role assignments, Somtoday Connect credentials, and at least one institution UUID.

## Provisioned resources

`infra/main.bicep` provisions:

- a StorageV2 account with Blob versioning, seven-day general previous-version lifecycle retention, one-day staging base/version lifecycle eligibility, seven-day Blob soft delete, and a private Blob container;
- Log Analytics;
- a Consumption Container Apps environment;
- a scheduled Container Apps Job with a system-assigned managed identity and a Somtoday client-secret.

The identity receives `Storage Blob Data Contributor` on the output container. The Job secret is mapped to `Somtoday__ClientSecret`; it is not accessed through the managed identity.

Defaults are public `latest` image, `0 4,16 * * *` UTC schedule (04:00 and 16:00 UTC), one replica, 0.25 vCPU, 1 GiB memory, 3,600-second Job timeout, and one Container Apps retry. Pin production deployments to a release tag or digest. Container Apps cron schedules are evaluated in UTC.

All resources use the region of the selected resource group. The Azure Portal already supplies that standard **Region** field, so the template no longer displays a separate location parameter with the unevaluated `resourceGroup().location` expression.

Resources that support tags receive the native tags of the selected resource group. Manage those tags on the resource group in Azure Portal; the template has no separate tags parameter.

Output is separated by institution by default and combines selected locations within each institution. The `separateByInstitution` and `separateByLocation` deployment parameters map to the application's independent output-layout settings.

## Blob versions, retention, and run overlap

The publication rollback path requires Blob versioning. The supplied Bicep and derived ARM template enable it and retain the general lifecycle rule that deletes previous versions after seven days. Existing seven-day Blob soft delete can keep a version temporarily recoverable after lifecycle deletion; budget and privacy retention reviews must account for that additional period.

Bicep normalizes `outputFolder` by trimming whitespace, converting backslashes to slashes, dropping empty segments, trimming each segment, and rejecting `.` and `..`. The same normalized value configures runtime `Output__Folder` and the lifecycle prefix `{container}/{normalizedOutputFolder}/.staging/`. For block Blobs under that prefix, current base Blobs become eligible for deletion after more than one day since modification and previous versions become eligible after more than one day since creation. Lifecycle execution is asynchronous, and the seven-day soft-delete policy can keep lifecycle-deleted staging temporarily recoverable. The application nevertheless treats all staging as current-run, current-dataset work data and never uses it for rollback or a later run. The rule has no Blob Index Tag filter.

If runtime `Output__Folder` is overridden manually outside the deployment template, redeploy the lifecycle policy with exactly the same normalized folder. Otherwise the policy does not cover the runtime staging location.

Redeploy the template to existing installations before running an application version that uses version-based rollback. Do not disable versioning or remove the lifecycle policy independently of the application publication contract.

The application uses one shared staging root at `{Output:Folder}/.staging/{RunId}/{FileName}`; there is no separate staging tree for V1, V2.1, or a live scope. Dataset publication remains sequential, and every next dataset overwrites its complete reused staging set before promotion. The application recognizes application-owned staging only by the exact lowercase `.staging/{compact UUIDv7}/{file}` tail plus producer metadata. Startup cleanup also recognizes legacy staging below live prefixes. It makes one cleanup attempt plus three complete retries at startup and after each dataset, trying every applicable Blob in each attempt. Exhausted cleanup emits a warning but does not fail the run, change a successful publication, roll back live output, or prevent later datasets. Remaining staging can contain personal data until a later startup or the storage lifecycle removes it, so operators must monitor cleanup warnings.

The first live institution/location segment below `Output:Folder` cannot equal `.staging` in any casing. Check before deployment that no legitimate live output currently occupies `{Output:Folder}/.staging/`.

Startup cleanup recovers staging space after an interrupted run but also means overlapping scheduled, manual, or retried runs are unsupported: it may remove the other run's staging data. Keep one active Job execution at a time.

## Power Automate School Data Sync handoff

The public [Power Automate template for automating School Data Sync CSV uploads through SFTP](https://make.powerautomate.com/galleries/public/templates/3c1ff79158b34374b9ee3c683abb5b55/) can be used as a starting point. This repository does not provision or operate the flow or the Microsoft SDS import.

For the storage account supplied by this deployment, use the [Azure Blob Storage connector](https://learn.microsoft.com/en-us/connectors/azureblob/) rather than SFTP-SSH. Make an operator-owned copy of the public flow; changing only its connection is insufficient because the connector action schemas differ. Confirm that the destination action uses the current `Microsoft School Data Sync V2` connector, not SDS Classic. Replace SFTP listing and reading with `Lists blobs (V2)` and `Get blob content using path (V2)`.

The current SDS experience accepts [both the SDS V1 and V2.1 CSV formats](https://learn.microsoft.com/en-us/schooldatasync/automate-csv-upload). Configure each flow instance with exactly one live V1 or V2.1 dataset directory and an SDS Connect data-Flow ID configured for that same format. Never combine versions. Every execution must send the same complete file set used for that Flow ID's initial upload, including configured guardian files. Retain a recurrence-based start, exclude `.staging`, and run only after the Container Apps Job completed successfully. The deployment provides no success marker, completion signal, or flow orchestration, so the operator must coordinate this ordering. A per-Blob trigger is unsuitable for a complete dataset, and the V2 Blob trigger does not monitor nested folders from one parent-folder trigger.

The supplied Bicep and generated ARM template are compatible with this approach as follows:

- The storage account is a StorageV2 account with a private Blob container and no configured Storage firewall or private endpoint.
- `allowSharedKeyAccess` is `false`, so use Microsoft Entra ID or service-principal authentication and the V2 connector actions. Access-key authentication cannot be used.
- Assign the Power Automate connection user or service principal at least `Storage Blob Data Reader` on the output container. The deployment assigns `Storage Blob Data Contributor` only to the Container Apps Job identity and does not provision a Power Automate connection or its role assignment.
- Configure the full Blob service URI, container, and one live dataset path in the flow. The Azure Blob Storage connector is a Power Automate Premium connector.
- Do not add a Storage firewall for this integration. Microsoft documents Power Platform access to storage accounts behind firewalls as unsupported and warns that a currently working connection might stop working; connector outbound-IP allowlisting does not change that support boundary.

Do not enable Azure Blob Storage SFTP or hierarchical namespace on this account. [Azure Blob Storage SFTP requires hierarchical namespace](https://learn.microsoft.com/en-us/azure/storage/blobs/secure-file-transfer-protocol-support), while [Blob versioning does not support hierarchical-namespace accounts](https://learn.microsoft.com/en-us/azure/storage/blobs/versioning-overview). The publication rollback contract requires versioning. Independently, the [managed SFTP-SSH connector lists Azure Blob Storage SFTP as unsupported](https://learn.microsoft.com/en-us/connectors/sftpwithssh/#general-known-issues-and-limitations).

The repository does not provision or export the Power Automate flow, SDS data-Flow ID, Entra application registration, connector connection, or manual `Storage Blob Data Reader` assignment.

## Azure Portal

The Deploy to Azure button in the Dutch [README](../../README.md) uses `infra/azuredeploy.json`. `infra/main.bicep` remains the source; do not edit the generated ARM JSON manually. CI verifies that both representations remain aligned.

1. Select the deployment button and choose subscription, resource group, and region.
2. Supply `schoolUuids` as a JSON array, `somtodayClientId`, and the required secure `somtodayClientSecret`; optionally choose `PROD`, `TEST`, or `ACCEPTATIE` and adjust the two output-layout parameters. NIGHTLY is not a production deployment parameter.
3. After RBAC propagation, start one manual run and inspect logs and Blob output.

The deployment does not create a free Azure subscription or grant Somtoday access.

## Azure CLI

Create a Git-ignored local parameter file:

```powershell
Copy-Item infra/main.example.bicepparam infra/main.bicepparam
# Set schoolUuids and somtodayClientId.

az group create --name rg-somtoday-sds --location westeurope
$env:SOMTODAY_CLIENT_SECRET = Read-Host 'Somtoday client secret' -MaskInput
az deployment group create --resource-group rg-somtoday-sds --parameters infra/main.bicepparam
```

Start and inspect a run:

```powershell
az containerapp job start --name somtodaysds-job --resource-group rg-somtoday-sds
az containerapp job execution list --name somtodaysds-job --resource-group rg-somtoday-sds --output table
az containerapp job logs show --name somtodaysds-job --resource-group rg-somtoday-sds --container somtoday2microsoftsds --follow --tail 100
```

Remove the shell value when the deployment is complete:

```powershell
Remove-Item Env:SOMTODAY_CLIENT_SECRET
```

## Privacy, cost, and responsibility

Azure resources can incur costs. The deploying school or Azure partner is responsible for purpose limitation, lawful basis, processor agreements, access controls, retention, security, data minimization, and other AVG/GDPR obligations. Review guardian behavior in the [export contract](../contracts/EXPORT.md) and follow the [security policy](../../SECURITY.md).
