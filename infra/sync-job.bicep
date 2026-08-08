extension microsoftGraphV1

targetScope = 'resourceGroup'

@description('Korte prefix voor deze Somtoday-syncjob. De uiteindelijke Job-naam wordt <prefix>-job. Maximaal 27 tekens.')
@minLength(1)
@maxLength(27)
param jobPrefix string

param environmentId string
param location string

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

var normalizedJobPrefix = toLower(trim(jobPrefix))
var jobName = '${normalizedJobPrefix}-job'
var imageReference = 'ghcr.io/essella/somtoday2microsoftsds:latest'
var containerName = 'somtoday2microsoftsds'
var replicaTimeoutSeconds = 3600
var replicaRetryLimit = 1

// Stabiele pseudo-random minuut tussen 0 en 59.
// guid() is deterministisch voor dezelfde resource group + jobnaam. De eerste twee hextekens
// vormen een waarde 0..255; modulo 60 verdeelt Jobs over de 60 mogelijke minuten.
var hexValues = {
  '0': 0
  '1': 1
  '2': 2
  '3': 3
  '4': 4
  '5': 5
  '6': 6
  '7': 7
  '8': 8
  '9': 9
  a: 10
  b: 11
  c: 12
  d: 13
  e: 14
  f: 15
}
var cronHash = guid(resourceGroup().id, jobName)
var cronHigh = hexValues[substring(cronHash, 0, 1)]
var cronLow = hexValues[substring(cronHash, 1, 1)]
var cronMinute = ((cronHigh * 16) + cronLow) % 60
var cronExpression = '${cronMinute} 2,14 * * *'

var schoolUuids = [for value in split(schoolUuidsCsv, ','): trim(value)]
var normalizedIncludedLocationCodes = [for value in split(includedLocationCodesCsv, ','): trim(value)]
var normalizedExcludedLocationCodes = [for value in split(excludedLocationCodesCsv, ','): trim(value)]
var includedLocationCodes = filter(normalizedIncludedLocationCodes, locationCode => !empty(locationCode))
var excludedLocationCodes = filter(normalizedExcludedLocationCodes, locationCode => !empty(locationCode))
var validatedInboundFlowId = length(inboundFlowId) == 36 ? inboundFlowId : fail('inboundFlowId must be a UUID.')
var invalidSchoolUuids = filter(schoolUuids, uuid => length(uuid) != 36)
var validatedSchoolUuids = length(schoolUuids) > 0 && length(invalidSchoolUuids) == 0
  ? schoolUuids
  : fail('schoolUuidsCsv must contain one or more comma-separated UUIDs.')

var jobTags = {
  'Somtoday2MicrosoftSDS.instance': normalizedJobPrefix
  'Somtoday2MicrosoftSDS.somtodayEnvironment': somtodayEnvironment
  'Somtoday2MicrosoftSDS.inboundFlowId': validatedInboundFlowId
  'Somtoday2MicrosoftSDS.schoolUuidCount': string(length(validatedSchoolUuids))
  'Somtoday2MicrosoftSDS.cron': cronExpression
}

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

var schoolEnvironmentVariables = [for (schoolUuid, index) in validatedSchoolUuids: {
  name: 'Somtoday__SchoolUUID__${index}'
  value: schoolUuid
}]

var includedLocationEnvironmentVariables = [for (locationCode, index) in includedLocationCodes: {
  name: 'Locations__IncludedLocationCodes__${index}'
  value: locationCode
}]

var excludedLocationEnvironmentVariables = [for (locationCode, index) in excludedLocationCodes: {
  name: 'Locations__ExcludedLocationCodes__${index}'
  value: locationCode
}]

module scheduledJob './job.bicep' = {
  name: 'deploy-${jobName}'
  params: {
    jobName: jobName
    location: location
    tags: jobTags
    environmentId: environmentId
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
output cronExpression string = cronExpression
