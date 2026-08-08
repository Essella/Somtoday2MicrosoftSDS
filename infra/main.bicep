targetScope = 'resourceGroup'

@description('Volledige naam voor de Azure Container Apps Environment.')
@minLength(2)
@maxLength(60)
param environmentName string = 'prod-somtoday2sds-env'

@description('Naam voor de Log Analytics Workspace die bij deze Environment hoort.')
@minLength(4)
@maxLength(63)
param logAnalyticsName string = 'prod-somtoday2sds-log-analytics'

var environmentTagName = 'Somtoday2MicrosoftSDS.environment'
var existingResourceGroupTags = resourceGroup().tags ?? {}
var alreadyInitialized = contains(existingResourceGroupTags, environmentTagName) && !empty(trim(string(existingResourceGroupTags[environmentTagName])))
var validatedEnvironmentName = !alreadyInitialized
  ? toLower(trim(environmentName))
  : fail('Deze resource group bevat al een Somtoday2MicrosoftSDS-environment. Maak syncjobs met deploy-sync-job.bicep.')

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: resourceGroup().location
  properties: {
    retentionInDays: 30
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: validatedEnvironmentName
  location: resourceGroup().location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

resource installationTag 'Microsoft.Resources/tags@2024-11-01' = {
  name: 'default'
  properties: {
    tags: union(existingResourceGroupTags, {
      'Somtoday2MicrosoftSDS.environment': environment.name
    })
  }
}

output containerAppsEnvironmentName string = environment.name
output deployedLogAnalyticsName string = logAnalytics.name
