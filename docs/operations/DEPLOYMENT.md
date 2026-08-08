# Azure deployment guide

## Deployment model

First deploy `infra/main.bicep` to a resource group. It creates Log Analytics, one Container Apps Environment, and the resource-group tag `Somtoday2MicrosoftSDS.environment`. Its only parameters are `environmentName` and `logAnalyticsName`. It creates no Job and no Microsoft Graph resource. Use the README Portal button for this step.

Then deploy `infra/deploy-sync-job.bicep` to the same resource group for each scheduled sync Job. The checked-in `infra/azuredeploy-sync-job.json` supports Azure Portal deployment. The Bicep file reads the Environment name from the tag in the same resource group. It does not accept an Environment name or resource ID. A Job deployment fails if the tag is absent.

The templates create no Storage Account, Power Automate flow, app registration, user-assigned identity, or permanent CSV output. Each Job uses a system-assigned managed identity. The secure `somtodayClientSecret` parameter becomes a Container Apps Job secret.

The Job image is fixed to `ghcr.io/essella/somtoday2microsoftsds:latest`. The schedule runs at 02:00 and 14:00 UTC with a deterministic minute from 0 through 59. The minute is derived from the resource group and Job name. The timeout is 3,600 seconds and the retry limit is one.

## Microsoft Graph permissions for Job deployments

Assign these Microsoft Graph application roles to every Job identity:

- `IndustryData-DataConnector.Read.All`
- `IndustryData-DataConnector.Upload`
- `IndustryData.ReadBasic.All` (read the validation operation returned in `Location`)

Use `infra/assign-sync-job-roles.ps1` to assign the roles automatically. It scans only visible resource groups with the `Somtoday2MicrosoftSDS.environment` tag and only Jobs with the `Somtoday2MicrosoftSDS.instance` tag. It adds only missing assignments, so it is safe to run again after adding Jobs. The script requests delegated `Application.Read.All` and `AppRoleAssignment.ReadWrite.All` through Microsoft Graph PowerShell. The operator must approve the consent prompt and have a supported Microsoft Entra administrator role, such as Privileged Role Administrator, Application Administrator, Cloud Application Administrator, or Global Administrator.

Alternatively, assign the same three application roles manually with Microsoft Graph PowerShell or another Microsoft Graph client. Obtain the Job identity object ID from the Container Apps Job **Identity** page, use the Microsoft Graph service principal as the resource, and create one `appRoleAssignment` for each listed role. Azure CLI cannot request the required first-party Graph scopes for this action. Microsoft Entra replication can make the role assignment fail immediately after Job creation. Run the script again after propagation; the assignments are idempotent.

The runtime uses Microsoft Graph `/beta` industry-data endpoints. Beta APIs can change and are not covered by the same compatibility guarantees as v1.0; review Microsoft changes before production upgrades.

## Portal deployments

The README has two Portal buttons. `infra/azuredeploy.json` is generated from `infra/main.bicep` and creates only the Environment and Log Analytics Workspace. `infra/azuredeploy-sync-job.json` is generated from `infra/deploy-sync-job.bicep` and creates only one Job. Neither template contains Microsoft Graph resources.

## Cloud Shell: assign roles to all Jobs

```powershell
$ref = 'main' # Use a release tag for production.
$script = Invoke-RestMethod "https://raw.githubusercontent.com/Essella/Somtoday2MicrosoftSDS/$ref/infra/assign-sync-job-roles.ps1"
& ([scriptblock]::Create($script))
```

The script installs `Microsoft.Graph.Authentication` for the current Cloud Shell user when required and opens a Graph consent prompt. It never reads or writes Somtoday credentials. Start and inspect a Job with `az containerapp job start`, `az containerapp job execution list`, and `az containerapp job logs show`. Never log the temporary upload session URL.

Azure resources can incur costs. The deploying school or partner remains responsible for tenant governance, privacy, access reviews, monitoring, and AVG/GDPR compliance.
