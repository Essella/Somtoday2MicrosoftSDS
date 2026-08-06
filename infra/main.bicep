extension microsoftGraphV1

targetScope = 'resourceGroup'

@description('ACA-omgevingsmodus. new maakt een nieuwe omgeving met Log Analytics; existing gebruikt de opgegeven bestaande omgeving.')
@allowed([
  'new'
  'existing'
])
param environmentMode string = 'new'

@description('Korte prefix voor nieuwe resourcenamen; standaard: somtodaysds.')
@minLength(3)
@maxLength(20)
param namePrefix string = 'somtodaysds'

@description('Naam van de nieuwe Container Apps Environment. Alleen gebruikt bij environmentMode new.')
param containerAppsEnvironmentName string = '${namePrefix}-env'

@description('Volledige resource-ID van een bestaande Container Apps Environment in dezelfde subscription. Verplicht bij environmentMode existing.')
param existingContainerAppsEnvironmentResourceId string = ''

@description('Naam van deze Container Apps Job.')
param jobName string = '${namePrefix}-job'

@description('Containerimage uit GHCR, inclusief release-tag of digest.')
param imageReference string = 'ghcr.io/essella/somtoday2microsoftsds:latest'

@description('Cron-schema in UTC voor de Job.')
param cronExpression string = '0 3,15 * * *'

@minValue(1)
@maxValue(3600)
param replicaTimeoutSeconds int = 3600

@minValue(0)
param replicaRetryLimit int = 1

@description('Somtoday-instellings-UUIDs die samen één volledige dataset vormen.')
@minLength(1)
param schoolUuids array = [
  '11111111-1111-1111-1111-111111111111'
]

@description('Inbound-flow-ID van Microsoft School Data Sync. Dit is niet de connector-ID.')
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

@description('OAuth-clientsecret van Somtoday Connect. Wordt opgeslagen als Job-secret.')
@secure()
@minLength(1)
param somtodayClientSecret string

param includedLocationCodes array = []
param excludedLocationCodes array = []
param enableGuardianSync bool = false
param teacherUsernameFormat string = 'Emailadres'
param studentUsernameFormat string = 'Emailadres'

var normalizedPrefix = toLower(namePrefix)
var logAnalyticsName = '${normalizedPrefix}-logs'
var containerName = 'somtoday2microsoftsds'
var isNewEnvironment = environmentMode == 'new'
var existingEnvironmentId = trim(existingContainerAppsEnvironmentResourceId)
var candidateExistingEnvironmentSegments = split(existingEnvironmentId, '/')
var isValidExistingEnvironmentId = length(candidateExistingEnvironmentSegments) == 9 && toLower(candidateExistingEnvironmentSegments[?1] ?? '') == 'subscriptions' && toLower(candidateExistingEnvironmentSegments[?2] ?? '') == toLower(subscription().subscriptionId) && toLower(candidateExistingEnvironmentSegments[?3] ?? '') == 'resourcegroups' && !empty(candidateExistingEnvironmentSegments[?4] ?? '') && toLower(candidateExistingEnvironmentSegments[?5] ?? '') == 'providers' && toLower(candidateExistingEnvironmentSegments[?6] ?? '') == 'microsoft.app' && toLower(candidateExistingEnvironmentSegments[?7] ?? '') == 'managedenvironments' && !empty(candidateExistingEnvironmentSegments[?8] ?? '')
var validatedExistingEnvironmentId = isNewEnvironment
  ? ''
  : isValidExistingEnvironmentId
    ? existingEnvironmentId
    : fail('existingContainerAppsEnvironmentResourceId must be a full Microsoft.App/managedEnvironments resource ID in the current subscription.')
var existingEnvironmentSegments = split(validatedExistingEnvironmentId, '/')
var existingEnvironmentResourceGroup = isNewEnvironment ? '' : existingEnvironmentSegments[4]
var existingEnvironmentName = isNewEnvironment ? '' : last(existingEnvironmentSegments)
var validatedInboundFlowId = length(inboundFlowId) == 36
  ? inboundFlowId
  : fail('inboundFlowId must be a UUID.')

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = if (isNewEnvironment) {
  name: logAnalyticsName
  location: resourceGroup().location
  tags: resourceGroup().tags
  properties: {
    retentionInDays: 30
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

resource newEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = if (isNewEnvironment) {
  name: containerAppsEnvironmentName
  location: resourceGroup().location
  tags: resourceGroup().tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics!.properties.customerId
        sharedKey: logAnalytics!.listKeys().primarySharedKey
      }
    }
  }
}

resource existingEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' existing = if (!isNewEnvironment) {
  scope: resourceGroup(existingEnvironmentResourceGroup)
  name: existingEnvironmentName
}

var effectiveEnvironmentId = isNewEnvironment ? newEnvironment!.id : existingEnvironment!.id
var effectiveLocation = isNewEnvironment ? resourceGroup().location : existingEnvironment!.location

var baseEnvironmentVariables = [
  {
    name: 'DOTNET_ENVIRONMENT'
    value: 'Production'
  }
  {
    name: 'AZURE_TOKEN_CREDENTIALS'
    value: 'ManagedIdentityCredential'
  }
  {
    name: 'Somtoday__ClientId'
    value: somtodayClientId
  }
  {
    name: 'Somtoday__Environment'
    value: somtodayEnvironment
  }
  {
    name: 'SchoolDataSync__InboundFlowId'
    value: validatedInboundFlowId
  }
  {
    name: 'SchoolDataSync__EnableGuardianSync'
    value: string(enableGuardianSync)
  }
  {
    name: 'UsernameFormat__Teacher'
    value: teacherUsernameFormat
  }
  {
    name: 'UsernameFormat__Student'
    value: studentUsernameFormat
  }
]

var schoolEnvironmentVariables = [for (schoolUuid, index) in schoolUuids: {
  name: 'Somtoday__SchoolUUID__${index}'
  value: string(schoolUuid)
}]

var includedLocationEnvironmentVariables = [for (locationCode, index) in includedLocationCodes: {
  name: 'Locations__IncludedLocationCodes__${index}'
  value: string(locationCode)
}]

var excludedLocationEnvironmentVariables = [for (locationCode, index) in excludedLocationCodes: {
  name: 'Locations__ExcludedLocationCodes__${index}'
  value: string(locationCode)
}]

module scheduledJob './job.bicep' = {
  name: 'deploy-${jobName}'
  params: {
    jobName: jobName
    location: effectiveLocation
    tags: resourceGroup().tags
    environmentId: effectiveEnvironmentId
    imageReference: imageReference
    containerName: containerName
    cronExpression: cronExpression
    replicaTimeoutSeconds: replicaTimeoutSeconds
    replicaRetryLimit: replicaRetryLimit
    somtodayClientSecret: somtodayClientSecret
    environmentVariables: concat(
      baseEnvironmentVariables,
      schoolEnvironmentVariables,
      includedLocationEnvironmentVariables,
      excludedLocationEnvironmentVariables
    )
  }
}

resource microsoftGraph 'Microsoft.Graph/servicePrincipals@v1.0' existing = {
  appId: '00000003-0000-0000-c000-000000000000'
}

var inboundFlowReadWriteRoles = filter(microsoftGraph.appRoles, role => role.value == 'IndustryData-InboundFlow.ReadWrite.All')
var connectorUploadRoles = filter(microsoftGraph.appRoles, role => role.value == 'IndustryData-DataConnector.Upload')
var operationReadRoles = filter(microsoftGraph.appRoles, role => role.value == 'IndustryData.ReadBasic.All')
var inboundFlowReadWriteRoleId = length(inboundFlowReadWriteRoles) == 1
  ? first(inboundFlowReadWriteRoles)!.id
  : fail('Microsoft Graph must expose exactly one IndustryData-InboundFlow.ReadWrite.All application role.')
var connectorUploadRoleId = length(connectorUploadRoles) == 1
  ? first(connectorUploadRoles)!.id
  : fail('Microsoft Graph must expose exactly one IndustryData-DataConnector.Upload application role.')
var operationReadRoleId = length(operationReadRoles) == 1
  ? first(operationReadRoles)!.id
  : fail('Microsoft Graph must expose exactly one IndustryData.ReadBasic.All application role.')

resource inboundFlowReadWriteRoleAssignment 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  appRoleId: inboundFlowReadWriteRoleId
  principalId: scheduledJob.outputs.principalId
  resourceId: microsoftGraph.id
  resourceDisplayName: microsoftGraph.displayName
}

resource connectorUploadRoleAssignment 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  appRoleId: connectorUploadRoleId
  principalId: scheduledJob.outputs.principalId
  resourceId: microsoftGraph.id
  resourceDisplayName: microsoftGraph.displayName
}

resource operationReadRoleAssignment 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  appRoleId: operationReadRoleId
  principalId: scheduledJob.outputs.principalId
  resourceId: microsoftGraph.id
  resourceDisplayName: microsoftGraph.displayName
}

output deployedJobName string = scheduledJob.outputs.jobName
output deployedContainerAppsEnvironmentResourceId string = effectiveEnvironmentId
