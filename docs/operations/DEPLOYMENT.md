# Azure deployment guide

## Deployment model

Each deployment creates exactly one scheduled Azure Container Apps Job for one SDS inbound flow. The Job may contain multiple Somtoday institution UUIDs. Deploy another Job separately for another inbound flow; Jobs may share an existing Container Apps Environment.

`environmentMode` controls the environment:

- `existing` (default) places the Job in the environment identified by `existingContainerAppsEnvironmentResourceId`. Supply the full `Microsoft.App/managedEnvironments` resource ID. It may be in another resource group, but must be in the same subscription.
- `new` creates Log Analytics and a new Container Apps Environment only after the operator explicitly selects that mode.

The default promotes environment reuse and avoids creating infrastructure, and its related cost, without an explicit choice. A deployment with the default mode cannot continue until it has a valid existing environment resource ID.

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

The README button submits the checked-in `infra/azuredeploy.json` directly to Azure Portal. Bicep remains the source. The generic ARM form cannot show a resource picker. Select `existing` and paste the full environment resource ID, or explicitly select `new` when no reusable environment exists. Supply `inboundFlowId`, `schoolUuids`, Somtoday client ID and secret, and pin production to a release tag or image digest.

Schedules are UTC. Defaults are 02:05 and 14:05 UTC, 0.5 vCPU, 1 GiB, a 3,600-second timeout, one replica, and one retry.

## Portal form with an environment picker

`infra/uiFormDefinition.json` is a Form View for an Azure Template Spec. It defaults to reusing an environment and uses `Microsoft.Solutions.ResourceSelector` to list `Microsoft.App/managedEnvironments` resources in the selected subscription. Selecting `new` hides the picker and shows the new-environment fields instead.

Publish a versioned Template Spec in the operator's Azure organization:

```powershell
az ts create `
  --name somtoday2microsoftsds `
  --version 1.0.0 `
  --resource-group rg-template-specs `
  --location westeurope `
  --template-file infra/azuredeploy.json `
  --ui-form-definition infra/uiFormDefinition.json
```

Deploy the published version from its Template Spec page in Azure Portal. The portal starts the Form View automatically. Grant intended operators read access to the Template Spec and the normal deployment permissions on their target scope. A Template Spec is organization-scoped and therefore does not replace the public, cross-tenant README button.

## Azure CLI

First Job when no reusable environment exists:

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
