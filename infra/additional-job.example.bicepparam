using './main.bicep'

param environmentMode = 'existing'
param namePrefix = 'somtodaysds-extra'
param existingContainerAppsEnvironmentResourceId = '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-somtoday-sds/providers/Microsoft.App/managedEnvironments/somtodaysds-env'
param jobName = 'somtodaysds-second-job'
param schoolUuids = [
  '44444444-4444-4444-4444-444444444444'
]
param inboundFlowId = '55555555-5555-5555-5555-555555555555'
param somtodayClientId = 'replace-with-somtoday-client-id'
param somtodayClientSecret = readEnvironmentVariable('SOMTODAY_CLIENT_SECRET')
