using './main.bicep'

param namePrefix = 'somtodaysds'
param location = 'westeurope'
param imageReference = 'ghcr.io/essella/somtoday2microsoftsds:latest'
param cronExpression = '0 1 * * *'
param schoolUuids = [
  '00000000-0000-0000-0000-000000000000'
]
param somtodayClientId = '00000000-0000-0000-0000-000000000000'
param somtodayEnvironment = 'PROD'
param separateByInstitution = true
param separateByLocation = false

// Wordt uitsluitend tijdens compilatie uit de huidige procesenvironment gelezen.
// Zonder de environment variable blijft de secure parameter leeg en verwijdert een
// volgende deployment het tijdelijke Container Apps-secret en de env-verwijzing.
param somtodayClientSecret = readEnvironmentVariable('SOMTODAY_BOOTSTRAP_SECRET', '')
