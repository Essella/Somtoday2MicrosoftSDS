# Azure deployment guide

## Supported production deployment

The only supported production deployment is a scheduled Azure Container Apps Job using the Linux container image. Local execution is for development and testing; a Windows executable is not supported.

Operators need an Azure subscription, permission to create resources and role assignments, Somtoday Connect credentials, and at least one institution UUID.

## Provisioned resources

`infra/main.bicep` provisions:

- a StorageV2 account and private Blob container;
- a Key Vault using RBAC, soft delete, and purge protection;
- Log Analytics;
- a Consumption Container Apps environment;
- a scheduled Container Apps Job with a system-assigned managed identity.

The identity receives `Storage Blob Data Contributor` on the output container and `Key Vault Secrets Officer` on the application Vault.

Defaults are public `latest` image, `0 1 * * *` UTC schedule, one replica, 0.5 vCPU, 1 GiB memory, 3,600-second Job timeout, and one Container Apps retry. Pin production deployments to a release tag or digest. Container Apps cron schedules are evaluated in UTC.

Output is separated by institution by default and combines selected locations within each institution. The `separateByInstitution` and `separateByLocation` deployment parameters map to the application's independent output-layout settings.

## Azure Portal

The Deploy to Azure button in the Dutch [README](../../README.md) uses `infra/azuredeploy.json`. `infra/main.bicep` remains the source; do not edit the generated ARM JSON manually. CI verifies that both representations remain aligned.

1. Select the deployment button and choose subscription, resource group, and region.
2. Supply at least `schoolUuids` as a JSON array and `somtodayClientId`; optionally adjust the two output-layout parameters.
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
