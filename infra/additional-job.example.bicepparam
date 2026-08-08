using './additional-job.bicep'

param jobPrefix = 'somtodaysds-second'
param schoolUuidsCsv = '44444444-4444-4444-4444-444444444444'
param inboundFlowId = '55555555-5555-5555-5555-555555555555'
param somtodayClientId = 'replace-with-somtoday-client-id'
param somtodayClientSecret = readEnvironmentVariable('SOMTODAY_CLIENT_SECRET')
