using './deploy-sync-job.bicep'

param jobPrefix = 'somtodaysds-second'
param schoolUuidsCsv = '44444444-4444-4444-4444-444444444444'
param sourceName = 'Example SDS source'
param somtodayClientId = 'replace-with-somtoday-client-id'
param somtodayClientSecret = readEnvironmentVariable('SOMTODAY_CLIENT_SECRET')
