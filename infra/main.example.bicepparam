using './main.bicep'

param namePrefix = 'somtodaysds'
param imageReference = 'ghcr.io/essella/somtoday2microsoftsds:latest'
param cronExpression = '0 3,15 * * *'
param schoolUuids = [
  '11111111-1111-1111-1111-111111111111'
  '22222222-2222-2222-2222-222222222222'
]
param somtodayClientId = '00000000-0000-0000-0000-000000000000'
param somtodayEnvironment = 'PROD'
param separateByInstitution = true
param separateByLocation = false

// Houdt dit voorbeeld compileerbaar zonder lokaal secret. De template weigert
// deze marker bij deployment; stel SOMTODAY_CLIENT_SECRET daarom vooraf in.
param somtodayClientSecret = readEnvironmentVariable('SOMTODAY_CLIENT_SECRET', '__SOMTODAY_CLIENT_SECRET_REQUIRED__')
