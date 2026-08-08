targetScope = 'resourceGroup'

@description('Volledige naam voor de gedeelde Azure Container Apps Environment.')
@minLength(2)
@maxLength(60)
param environmentName string

@description('Korte prefix voor de eerste Somtoday-syncjob. De uiteindelijke Job-naam wordt <prefix>-job. Maximaal 27 tekens.')
@minLength(1)
@maxLength(27)
param jobPrefix string

@description('Somtoday-instellings-UUIDs, door komma\'s gescheiden. Meerdere UUIDs vormen samen één SDS-dataset.')
@minLength(36)
param schoolUuidsCsv string

@description('Inbound-flow-ID van Microsoft School Data Sync. Dit is niet de connector-ID.')
@minLength(36)
@maxLength(36)
param inboundFlowId string

@description('OAuth-client-ID van Somtoday Connect.')
@minLength(1)
param somtodayClientId string

@allowed([
  'PROD'
  'TEST'
  'ACCEPTATIE'
])
param somtodayEnvironment string = 'PROD'

@description('OAuth-clientsecret van Somtoday Connect. Wordt uitsluitend als Container Apps Job-secret opgeslagen.')
@secure()
@minLength(1)
param somtodayClientSecret string

@description('Optionele opgenomen locatiecodes, door komma\'s gescheiden. Leeg betekent geen inclusiefilter.')
param includedLocationCodesCsv string = ''

@description('Optionele uitgesloten locatiecodes, door komma\'s gescheiden. Leeg betekent geen exclusiefilter.')
param excludedLocationCodesCsv string = ''

param enableGuardianSync bool = false
param teacherUsernameFormat string = 'Emailadres'
param studentUsernameFormat string = 'Emailadres'

var environmentTagName = 'Somtoday2MicrosoftSDS.environment'
var existingResourceGroupTags = resourceGroup().tags ?? {}
var alreadyInitialized = contains(existingResourceGroupTags, environmentTagName) && !empty(trim(string(existingResourceGroupTags[environmentTagName])))
var validatedEnvironmentName = !alreadyInitialized
  ? toLower(trim(environmentName))
  : fail('Deze resource group bevat al een Somtoday2MicrosoftSDS-environment. Gebruik de knop "Add another sync job" om een extra Job toe te voegen.')

// De Log Analytics-naam is intern en stabiel; de eindgebruiker hoeft hiervoor geen extra naam op te geven.
var logAnalyticsName = 'log-${uniqueString(resourceGroup().id, validatedEnvironmentName)}'

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

// Eén enkele tag is voldoende voor vervolgdeployments. Subscription en resource group zijn al bekend
// uit de resource group die de gebruiker in Azure Portal selecteert.
resource installationTag 'Microsoft.Resources/tags@2024-11-01' = {
  name: 'default'
  properties: {
    tags: union(existingResourceGroupTags, {
      'Somtoday2MicrosoftSDS.environment': environment.name
    })
  }
}

module firstSyncJob './sync-job.bicep' = {
  name: 'first-sync-job'
  params: {
    jobPrefix: jobPrefix
    environmentId: environment.id
    location: environment.location
    schoolUuidsCsv: schoolUuidsCsv
    inboundFlowId: inboundFlowId
    somtodayClientId: somtodayClientId
    somtodayEnvironment: somtodayEnvironment
    somtodayClientSecret: somtodayClientSecret
    includedLocationCodesCsv: includedLocationCodesCsv
    excludedLocationCodesCsv: excludedLocationCodesCsv
    enableGuardianSync: enableGuardianSync
    teacherUsernameFormat: teacherUsernameFormat
    studentUsernameFormat: studentUsernameFormat
  }
}

output deployedJobName string = firstSyncJob.outputs.deployedJobName
output containerAppsEnvironmentName string = environment.name
output generatedCronExpression string = firstSyncJob.outputs.cronExpression
