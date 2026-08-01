# Azure deployment guide

## Supported production deployment

The only supported production deployment is a scheduled Azure Container Apps Job using the Linux container image. Local execution is for development and testing; a Windows executable is not supported.

Operators need an Azure subscription, permission to create resources and role assignments, Somtoday Connect credentials, and at least one institution UUID.

## Provisioned resources

`infra/main.bicep` provisions:

- a StorageV2 account with Blob versioning, seven-day previous-version lifecycle retention, seven-day Blob soft delete, and a private Blob container;
- a Key Vault using RBAC, soft delete, and purge protection;
- Log Analytics;
- a Consumption Container Apps environment;
- a scheduled Container Apps Job with a system-assigned managed identity.

The identity receives `Storage Blob Data Contributor` on the output container and `Key Vault Secrets Officer` on the application Vault.

Defaults are public `latest` image, `0 1 * * *` UTC schedule, one replica, 0.5 vCPU, 1 GiB memory, 3,600-second Job timeout, and one Container Apps retry. Pin production deployments to a release tag or digest. Container Apps cron schedules are evaluated in UTC.

Output is separated by institution by default and combines selected locations within each institution. The `separateByInstitution` and `separateByLocation` deployment parameters map to the application's independent output-layout settings.

## Blob versions, retention, and run overlap

The publication rollback path requires Blob versioning. The supplied Bicep and derived ARM template enable it and add a lifecycle rule that deletes previous versions after seven days. Existing seven-day Blob soft delete can keep a version temporarily recoverable after the lifecycle deletion; budget and privacy retention reviews must account for that additional period.

Redeploy the template to existing installations before running an application version that uses version-based rollback. Do not disable versioning or remove the lifecycle policy independently of the application publication contract.

The application recognizes application-owned staging only by the exact `.staging/{compact UUIDv7}/{file}` tail plus producer metadata. It makes one cleanup attempt plus three complete retries at startup and after each dataset, trying every applicable Blob in each attempt. Exhausted cleanup emits a warning but does not fail the run, change a successful publication, roll back live output, or prevent later datasets. Remaining staging can contain personal data until a later startup or the storage lifecycle removes it, so operators must monitor cleanup warnings.

Startup cleanup recovers staging space after an interrupted run but also means overlapping scheduled, manual, or retried runs are unsupported: it may remove the other run's staging data. Keep one active Job execution at a time.

## Azure Portal

The Deploy to Azure button in the Dutch [README](../../README.md) uses `infra/azuredeploy.json`. `infra/main.bicep` remains the source; do not edit the generated ARM JSON manually. CI verifies that both representations remain aligned.

1. Select the deployment button and choose subscription, resource group, and region.
2. Supply at least `schoolUuids` as a JSON array and `somtodayClientId`; optionally choose `PROD`, `TEST`, or `ACCEPTATIE` and adjust the two output-layout parameters. NIGHTLY is not a production deployment parameter.
3. Supply `somtodayClientSecret` only for initial bootstrap or rotation.
4. After RBAC propagation, start one manual run and inspect logs and Blob output.
5. Redeploy the same resource group and `namePrefix` without `somtodayClientSecret`.

The deployment does not create a free Azure subscription or grant Somtoday access.

## Azure CLI

Create a Git-ignored local parameter file:

```powershell
Copy-Item infra/main.example.bicepparam infra/main.bicepparam
# Set schoolUuids and somtodayClientId.

az group create --name rg-somtoday-sds --location westeurope
az deployment group create --resource-group rg-somtoday-sds --parameters infra/main.bicepparam
```

For bootstrap or rotation, expose the value only to the current shell:

```powershell
$env:SOMTODAY_BOOTSTRAP_SECRET = Read-Host 'Somtoday client secret' -MaskInput
az deployment group create --resource-group rg-somtoday-sds --parameters infra/main.bicepparam
```

Start and inspect a run:

```powershell
az containerapp job start --name somtodaysds-job --resource-group rg-somtoday-sds
az containerapp job execution list --name somtodaysds-job --resource-group rg-somtoday-sds --output table
az containerapp job logs show --name somtodaysds-job --resource-group rg-somtoday-sds --container somtoday2microsoftsds --follow --tail 100
```

Then remove the bootstrap value and redeploy:

```powershell
Remove-Item Env:SOMTODAY_BOOTSTRAP_SECRET
az deployment group create --resource-group rg-somtoday-sds --parameters infra/main.bicepparam
```

## Privacy, cost, and responsibility

Azure resources can incur costs. The deploying school or Azure partner is responsible for purpose limitation, lawful basis, processor agreements, access controls, retention, security, data minimization, and other AVG/GDPR obligations. Review guardian behavior in the [export contract](../contracts/EXPORT.md) and follow the [security policy](../../SECURITY.md).
