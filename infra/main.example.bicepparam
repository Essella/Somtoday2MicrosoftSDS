using './main.bicep'

param environmentMode = 'new'
param namePrefix = 'somtodaysds'
param containerAppsEnvironmentName = 'somtodaysds-env'
param jobName = 'somtodaysds-job'
param schoolUuids = [
  '11111111-1111-1111-1111-111111111111'
  '22222222-2222-2222-2222-222222222222'
]
param inboundFlowId = '33333333-3333-3333-3333-333333333333'
param somtodayClientId = 'replace-with-somtoday-client-id'
param somtodayClientSecret = readEnvironmentVariable('SOMTODAY_CLIENT_SECRET')
