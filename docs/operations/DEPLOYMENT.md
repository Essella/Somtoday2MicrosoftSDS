# Azure deployment guide

## Deployment model

Each deployment creates exactly one scheduled Azure Container Apps Job for one SDS inbound flow. The Job may contain multiple Somtoday institution UUIDs. Deploy another Job separately for another inbound flow; Jobs may share an existing Container Apps Environment.

`environmentMode` controls the environment:

- `new` (default) creates Log Analytics and a new Container Apps Environment.
- `existing` places the Job in the environment identified by `existingContainerAppsEnvironmentResourceId`. Supply the full `Microsoft.App/managedEnvironments` resource ID. It may be in another resource group, but must be in the same subscription.

The template creates no Storage Account, Power Automate flow, app registration, user-assigned identity, or permanent CSV output. The Job uses only a system-assigned managed identity. The secure `somtodayClientSecret` parameter becomes a Container Apps Job secret.

## Microsoft Graph permissions

The Bicep Microsoft Graph extension assigns the Job identity these application roles on Microsoft Graph:

- `IndustryData-InboundFlow.ReadWrite.All`
- `IndustryData-DataConnector.Upload`
- `IndustryData.ReadBasic.All` (read the validation operation returned in `Location`)

The deploying principal therefore needs the normal Azure deployment rights plus delegated or application `Application.Read.All` and `AppRoleAssignment.ReadWrite.All` in Microsoft Entra ID, with tenant admin consent where required. The runtime identity does not need those deployment permissions.

Microsoft Entra replication can make the first deployment fail immediately after creation of the Job identity. Rerun the same deployment after propagation; it is idempotent. Microsoft Graph Bicep resources also have limited Portal deployment detail and do not support Azure what-if.

The runtime uses Microsoft Graph `/beta` industry-data endpoints. Beta APIs can change and are not covered by the same compatibility guarantees as v1.0; review Microsoft changes before production upgrades.

## Portal deployment

The README button submits the checked-in `infra/azuredeploy.json` directly to Azure Portal. Bicep remains the source. For the first Job keep `environmentMode=new`; for additional Jobs select `existing` and paste the first deployment's environment resource-ID. Supply `inboundFlowId`, `schoolUuids`, Somtoday client ID and secret, and pin production to a release tag or image digest.

Schedules are UTC. Defaults are 03:00 and 15:00 UTC, 0.5 vCPU, 1 GiB, a 3,600-second timeout, one replica, and one retry.

## Azure CLI

First Job:

```powershell
Copy-Item infra/main.example.bicepparam infra/main.bicepparam
$env:SOMTODAY_CLIENT_SECRET = Read-Host 'Somtoday client secret' -MaskInput
az deployment group create --resource-group rg-somtoday-sds --parameters infra/main.bicepparam
```

Additional Job in the same environment:

```powershell
Copy-Item infra/additional-job.example.bicepparam infra/additional-job.bicepparam
# Replace the environment resource ID, school UUIDs, inbound flow and client ID.
az deployment group create --resource-group rg-somtoday-sds --parameters infra/additional-job.bicepparam
Remove-Item Env:SOMTODAY_CLIENT_SECRET
```

Start and inspect a Job with `az containerapp job start`, `az containerapp job execution list`, and `az containerapp job logs show`. Never log the temporary upload session URL.

Azure resources can incur costs. The deploying school or partner remains responsible for tenant governance, privacy, access reviews, monitoring, and AVG/GDPR compliance.
