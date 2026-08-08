targetScope = 'resourceGroup'

@description('Korte prefix voor deze extra Somtoday-syncjob. De uiteindelijke Job-naam wordt prefix-job. Maximaal 27 tekens.')
@minLength(1)
@maxLength(27)
param jobPrefix string = 'prod-sds-profielnaam'

@description('Vaste, unieke naam van de Microsoft School Data Sync-bron.')
@minLength(1)
@maxLength(100)
param sourceName string = 'SchoolDataSync-profielnaam'

@allowed([
  'PROD'
  'TEST'
  'ACCEPTATIE'
])
param somtodayEnvironment string = 'PROD'

@description('Somtoday-instellings-UUIDs, door kommas gescheiden. Meerdere UUIDs vormen samen een SDS-dataset.')
@minLength(36)
param schoolUuidsCsv string = '11111111-1111-1111-1111-111111111111,22222222-2222-2222-2222-222222222222'

@description('OAuth-client-ID van Somtoday Connect.')
@minLength(1)
param somtodayClientId string = '00000000-0000-0000-0000-000000000000'

@description('OAuth-clientsecret van Somtoday Connect. Wordt uitsluitend als Container Apps Job-secret opgeslagen.')
@secure()
@minLength(1)
param somtodayClientSecret string

@description('Optionele opgenomen locatiecodes, door kommas gescheiden. Leeg betekent geen inclusiefilter.')
param includedLocationCodesCsv string = ''

@description('Optionele uitgesloten locatiecodes, door kommas gescheiden. Leeg betekent geen exclusiefilter.')
param excludedLocationCodesCsv string = ''

param enableGuardianSync bool = false
param teacherUsernameFormat string = 'Emailadres'
param studentUsernameFormat string = 'Emailadres'

var environmentTagName = 'Somtoday2MicrosoftSDS.environment'
var resourceGroupTags = resourceGroup().tags ?? {}
var storedEnvironmentName = contains(resourceGroupTags, environmentTagName)
  ? trim(string(resourceGroupTags[environmentTagName]))
  : ''
var validatedEnvironmentName = !empty(storedEnvironmentName)
  ? storedEnvironmentName
  : fail('Deze resource group bevat geen Somtoday2MicrosoftSDS.environment-tag. Maak eerst de Environment met main.bicep.')

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: validatedEnvironmentName
}

module syncJob './sync-job.bicep' = {
  name: 'deploy-sync-job'
  params: {
    jobPrefix: jobPrefix
    environmentId: environment.id
    location: environment.location
    sourceName: sourceName
    somtodayEnvironment: somtodayEnvironment
    schoolUuidsCsv: schoolUuidsCsv
    somtodayClientId: somtodayClientId
    somtodayClientSecret: somtodayClientSecret
    includedLocationCodesCsv: includedLocationCodesCsv
    excludedLocationCodesCsv: excludedLocationCodesCsv
    enableGuardianSync: enableGuardianSync
    teacherUsernameFormat: teacherUsernameFormat
    studentUsernameFormat: studentUsernameFormat
  }
}

output deployedJobName string = syncJob.outputs.deployedJobName
output containerAppsEnvironmentName string = environment.name
output generatedCronExpression string = syncJob.outputs.cronExpression
