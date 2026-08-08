# Azure deployment guide

## Deployment model

Each deployment creates exactly one scheduled Azure Container Apps Job for one SDS inbound flow. The Job can contain multiple Somtoday institution UUIDs. Deploy another Job separately for another inbound flow.

Use `infra/main.bicep` for the first deployment in a resource group. It creates Log Analytics, one Container Apps Environment, and the first Job. It stores the Environment name in the resource-group tag `Somtoday2MicrosoftSDS.environment`.

Use `infra/additional-job.bicep` only for an additional Job in the same resource group. It reads that tag and uses the stored Environment name. It does not accept an Environment name or resource ID. A deployment fails if the tag is absent. A first-deployment template fails if the tag already exists.

The template creates no Storage Account, Power Automate flow, app registration, user-assigned identity, or permanent CSV output. Each Job uses only a system-assigned managed identity. The secure `somtodayClientSecret` parameter becomes a Container Apps Job secret.

The Job image is fixed to `ghcr.io/essella/somtoday2microsoftsds:latest`. The schedule runs at 02:00 and 14:00 UTC with a deterministic minute from 0 through 59. The minute is derived from the resource group and Job name. The timeout is 3,600 seconds and the retry limit is one.

## Microsoft Graph permissions

The Bicep Microsoft Graph extension assigns the Job identity these application roles on Microsoft Graph:

- `IndustryData-InboundFlow.ReadWrite.All`
- `IndustryData-DataConnector.Upload`
- `IndustryData.ReadBasic.All` (read the validation operation returned in `Location`)

The deploying principal therefore needs the normal Azure deployment rights plus delegated or application `Application.Read.All` and `AppRoleAssignment.ReadWrite.All` in Microsoft Entra ID, with tenant admin consent where required. The runtime identity does not need those deployment permissions.

Microsoft Entra replication can make the first deployment fail immediately after creation of the system-assigned identity. Rerun the same deployment after propagation; it is idempotent. Microsoft Graph Bicep resources also have limited Portal deployment detail and do not support Azure what-if.

The runtime uses Microsoft Graph `/beta` industry-data endpoints. Beta APIs can change and are not covered by the same compatibility guarantees as v1.0; review Microsoft changes before production upgrades.

## Portal deployment

Use the **Deploy new environment + first sync job** button in the README for the first deployment. Use **Add another sync job** only in the same resource group after the first deployment succeeds. The second template reads the Environment name from the resource-group tag. It does not ask for an Environment name or resource ID.

The templates are checked-in generated ARM files. `infra/azuredeploy.json` is generated from `infra/main.bicep`. `infra/azuredeploy-additional-job.json` is generated from `infra/additional-job.bicep`.

## Azure CLI

First deployment:

```powershell
Copy-Item infra/main.example.bicepparam infra/main.bicepparam
$env:SOMTODAY_CLIENT_SECRET = Read-Host 'Somtoday client secret' -MaskInput
# Replace the Environment name, job prefix, school UUIDs, inbound flow, and client ID.
az deployment group create --resource-group rg-somtoday-sds --parameters infra/main.bicepparam
```

Additional Job in the same resource group:

```powershell
Copy-Item infra/additional-job.example.bicepparam infra/additional-job.bicepparam
# Replace the job prefix, school UUIDs, inbound flow, and client ID.
az deployment group create --resource-group rg-somtoday-sds --parameters infra/additional-job.bicepparam
Remove-Item Env:SOMTODAY_CLIENT_SECRET
```

Start and inspect a Job with `az containerapp job start`, `az containerapp job execution list`, and `az containerapp job logs show`. Never log the temporary upload session URL.

Azure resources can incur costs. The deploying school or partner remains responsible for tenant governance, privacy, access reviews, monitoring, and AVG/GDPR compliance.
