using './main.bicep'

param namePrefix = 'somtodaysds'
param imageReference = 'ghcr.io/essella/somtoday2microsoftsds:latest'
param cronExpression = '0 4,16 * * *'
param schoolUuids = [
  '00000000-0000-0000-0000-000000000000'
]
param somtodayClientId = '00000000-0000-0000-0000-000000000000'
param somtodayEnvironment = 'PROD'
param separateByInstitution = true
param separateByLocation = false

// Wordt uitsluitend tijdens compilatie uit de huidige procesenvironment gelezen.
// Stel SOMTODAY_CLIENT_SECRET in voordat u de deployment start. Een lege waarde
// faalt bij deployment, omdat de parameter verplicht is.
param somtodayClientSecret = readEnvironmentVariable('SOMTODAY_CLIENT_SECRET', '')
