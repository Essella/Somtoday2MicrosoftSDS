# Azure deployment guide

## Deployment model

First deploy `infra/main.bicep` to a resource group. It creates Log Analytics, one Container Apps Environment, and the resource-group tag `Somtoday2MicrosoftSDS.environment`. Its only parameters are `environmentName` and `logAnalyticsName`. It creates no Job and no Microsoft Graph resource. Use the README Portal button for this step.

Then deploy `infra/deploy-sync-job.bicep` with Azure CLI for each scheduled sync Job. The template reads the Environment name from the tag in the same resource group. It does not accept an Environment name or resource ID. A Job deployment fails if the tag is absent.

The templates create no Storage Account, Power Automate flow, app registration, user-assigned identity, or permanent CSV output. Each Job uses a system-assigned managed identity. The secure `somtodayClientSecret` parameter becomes a Container Apps Job secret.

The Job image is fixed to `ghcr.io/essella/somtoday2microsoftsds:latest`. The schedule runs at 02:00 and 14:00 UTC with a deterministic minute from 0 through 59. The minute is derived from the resource group and Job name. The timeout is 3,600 seconds and the retry limit is one.

## Microsoft Graph permissions for Job deployments

The Job template assigns these Microsoft Graph application roles to the Job identity:

- `IndustryData-InboundFlow.ReadWrite.All`
- `IndustryData-DataConnector.Upload`
- `IndustryData.ReadBasic.All` (read the validation operation returned in `Location`)

Run the Job deployment with Azure CLI or Azure PowerShell. The deploying principal needs normal Azure deployment rights plus delegated or application `Application.Read.All` and `AppRoleAssignment.ReadWrite.All` in Microsoft Entra ID, with tenant admin consent where required. The runtime identity does not need those deployment permissions.

Microsoft Entra replication can make a Job deployment fail immediately after creation of the system-assigned identity. Rerun the same Job deployment after propagation; it is idempotent. Microsoft Graph Bicep resources do not support Azure what-if.

The runtime uses Microsoft Graph `/beta` industry-data endpoints. Beta APIs can change and are not covered by the same compatibility guarantees as v1.0; review Microsoft changes before production upgrades.

## Portal deployment: Environment only

The README button submits the checked-in `infra/azuredeploy.json` to Azure Portal. This ARM template is generated from `infra/main.bicep`. It creates only Azure resources, so it does not require Microsoft Graph deployment permissions.

## Cloud Shell: Sync Job

```powershell
$ref = 'main' # Use a release tag for production.
$script = Invoke-RestMethod "https://raw.githubusercontent.com/Essella/Somtoday2MicrosoftSDS/$ref/infra/deploy-sync-job.ps1"
& ([scriptblock]::Create($script)) -RepositoryRef $ref
```

The script lists only visible resource groups that contain the `Somtoday2MicrosoftSDS.environment` tag. It verifies the tagged Environment before it asks for the Job settings. It downloads the required Bicep files to a temporary directory, passes the secret only through an environment variable, and removes the temporary files and variable when it finishes. Run it again with a different Job prefix for each additional inbound flow. Start and inspect a Job with `az containerapp job start`, `az containerapp job execution list`, and `az containerapp job logs show`. Never log the temporary upload session URL.

Azure resources can incur costs. The deploying school or partner remains responsible for tenant governance, privacy, access reviews, monitoring, and AVG/GDPR compliance.
