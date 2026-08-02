targetScope = 'resourceGroup'

@description('Korte prefix voor de namen van alle resources. Gebruik 3-12 kleine letters, cijfers of koppeltekens; standaard: somtodaysds.')
@minLength(3)
@maxLength(12)
param namePrefix string = 'somtodaysds'

@description('Containerimage uit GHCR, inclusief release-tag of digest. Gebruik in productie bij voorkeur een vaste tag of digest; standaard: latest.')
param imageReference string = 'ghcr.io/essella/somtoday2microsoftsds:latest'

@description('Cron-schema in UTC voor de Job. Standaard: dagelijks om 03:00 en 15:00 UTC (0 3,15 * * *). Dat betekent 04:00 en 16:00 in Nederland (CET/CEST).')
param cronExpression string = '0 3,15 * * *'

@description('vCPU per Job-replica. Kies een ondersteunde waarde tussen 0.25 en 2.0; standaard: 0.25 vCPU.')
@allowed([
  '0.25'
  '0.5'
  '0.75'
  '1.0'
  '1.25'
  '1.5'
  '1.75'
  '2.0'
])
param cpu string = '0.25'

@description('Geheugen per Job-replica. Kies een ondersteunde waarde tussen 0.5 en 4 GiB; standaard: 1 GiB.')
@allowed([
  '0.5Gi'
  '1Gi'
  '1.5Gi'
  '2Gi'
  '2.5Gi'
  '3Gi'
  '3.5Gi'
  '4Gi'
])
param memory string = '1Gi'

@description('Maximale looptijd van een Job-replica in seconden (1-3600); standaard: 3600 seconden.')
@minValue(1)
@maxValue(3600)
param replicaTimeoutSeconds int = 3600

@description('Aantal extra pogingen na een mislukte Job-replica; standaard: 1.')
@minValue(0)
param replicaRetryLimit int = 1

@description('Verplicht: JSON-array met minimaal een Somtoday-instellings-UUID. Voorbeeld met een item: [\\"11111111-1111-1111-1111-111111111111\\"]. Voorbeeld met twee items: [\\"11111111-1111-1111-1111-111111111111\\", \\"22222222-2222-2222-2222-222222222222\\"].')
@minLength(1)
param schoolUuids array

@description('Verplicht: OAuth-client-ID van Somtoday Connect.')
@minLength(1)
param somtodayClientId string

@description('Somtoday-omgeving: PROD, TEST of ACCEPTATIE; standaard: PROD.')
@allowed([
  'PROD'
  'TEST'
  'ACCEPTATIE'
])
param somtodayEnvironment string = 'PROD'

@description('Verplicht: OAuth-clientsecret van Somtoday Connect. Wordt veilig opgeslagen als secret van de Container Apps Job.')
@secure()
@minLength(1)
param somtodayClientSecret string

@description('Optionele JSON-array met op te nemen locatiecodes. Voorbeeld: [\\"LOC1\\", \\"LOC2\\"]. Leeg betekent alle locaties.')
param includedLocationCodes array = []

@description('Optionele JSON-array met uit te sluiten locatiecodes. Voorbeeld: [\\"LOC1\\", \\"LOC2\\"]. Uitsluiting heeft voorrang op opname.')
param excludedLocationCodes array = []

@description('Exporteer SDS-guardiangebruikers en -relaties; standaard: false.')
param enableGuardianSync bool = false

@description('Gebruikersnaamexpressie voor medewerkers; standaard: Emailadres.')
param teacherUsernameFormat string = 'Emailadres'

@description('Gebruikersnaamexpressie voor leerlingen; standaard: Emailadres.')
param studentUsernameFormat string = 'Emailadres'

@description('Naam van de prive Blobcontainer voor de CSV-uitvoer; standaard: sds.')
param blobContainerName string = 'sds'

@description('Virtuele basismap in de Blobcontainer voor de uitvoer; standaard: sds/output.')
param outputFolder string = 'sds/output'

@description('Genereer altijd SDS-bestanden met alleen headers; standaard: false.')
param generateEmptyCsv bool = false

@description('Maak een afzonderlijke uitvoermap per Somtoday-instelling; standaard: true.')
param separateByInstitution bool = true

@description('Maak een afzonderlijke uitvoermap per Somtoday-vestiging; standaard: false.')
param separateByLocation bool = false

var normalizedPrefix = toLower(namePrefix)
var uniqueSuffix = uniqueString(subscription().subscriptionId, resourceGroup().id)
var storageAccountName = take('sds${replace(normalizedPrefix, '-', '')}${uniqueSuffix}', 24)
var logAnalyticsName = '${normalizedPrefix}-logs'
var environmentName = '${normalizedPrefix}-env'
var jobName = '${normalizedPrefix}-job'
var containerName = 'somtoday2microsoftsds'
var slashNormalizedOutputFolder = replace(trim(outputFolder), '\\', '/')
var normalizedOutputFolderSegments = map(
  filter(split(slashNormalizedOutputFolder, '/'), segment => !empty(trim(segment))),
  segment => trim(segment)
)
var normalizedOutputFolder = !empty(normalizedOutputFolderSegments) && !contains(normalizedOutputFolderSegments, '.') && !contains(
    normalizedOutputFolderSegments,
    '..'
  )
  ? join(normalizedOutputFolderSegments, '/')
  : fail('outputFolder must contain at least one segment and may not contain . or .. segments.')

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
    name: 'Storage__AzureBlob__ServiceUri'
    value: storageAccount.properties.primaryEndpoints.blob
  }
  {
    name: 'Storage__AzureBlob__Container'
    value: blobContainerName
  }
  {
    name: 'Output__Folder'
    value: normalizedOutputFolder
  }
  {
    name: 'Output__GenerateEmptyCsv'
    value: string(generateEmptyCsv)
  }
  {
    name: 'Output__SeparateByInstitution'
    value: string(separateByInstitution)
  }
  {
    name: 'Output__SeparateByLocation'
    value: string(separateByLocation)
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

var secretEnvironmentVariables = [
  {
    name: 'Somtoday__ClientSecret'
    secretRef: 'somtoday-client-secret'
  }
]

var jobSecrets = [
  {
    name: 'somtoday-client-secret'
    value: somtodayClientSecret
  }
]

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: resourceGroup().location
  tags: resourceGroup().tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    isVersioningEnabled: true
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource storageManagementPolicy 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          enabled: true
          name: 'delete-staging-after-one-day'
          type: 'Lifecycle'
          definition: {
            actions: {
              baseBlob: {
                delete: {
                  daysAfterModificationGreaterThan: 1
                }
              }
              version: {
                delete: {
                  daysAfterCreationGreaterThan: 1
                }
              }
            }
            filters: {
              blobTypes: [
                'blockBlob'
              ]
              prefixMatch: [
                '${blobContainerName}/${normalizedOutputFolder}/.staging/'
              ]
            }
          }
        }
        {
          enabled: true
          name: 'delete-previous-blob-versions-after-seven-days'
          type: 'Lifecycle'
          definition: {
            actions: {
              version: {
                delete: {
                  daysAfterCreationGreaterThan: 7
                }
              }
            }
            filters: {
              blobTypes: [
                'blockBlob'
              ]
              prefixMatch: [
                '${blobContainerName}/'
              ]
            }
          }
        }
      ]
    }
  }
}

resource outputContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: blobContainerName
  properties: {
    publicAccess: 'None'
  }
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
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

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: resourceGroup().location
  tags: resourceGroup().tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

resource scheduledJob 'Microsoft.App/jobs@2024-03-01' = {
  name: jobName
  location: resourceGroup().location
  tags: resourceGroup().tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    environmentId: containerAppsEnvironment.id
    configuration: {
      replicaRetryLimit: replicaRetryLimit
      replicaTimeout: replicaTimeoutSeconds
      triggerType: 'Schedule'
      scheduleTriggerConfig: {
        cronExpression: cronExpression
        parallelism: 1
        replicaCompletionCount: 1
      }
      secrets: jobSecrets
    }
    template: {
      containers: [
        {
          name: containerName
          image: imageReference
          env: concat(
            baseEnvironmentVariables,
            schoolEnvironmentVariables,
            includedLocationEnvironmentVariables,
            excludedLocationEnvironmentVariables,
            secretEnvironmentVariables
          )
          resources: {
            cpu: json(cpu)
            memory: memory
          }
        }
      ]
    }
  }
}

var storageBlobDataContributorRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
)

resource storageRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(outputContainer.id, scheduledJob.id, storageBlobDataContributorRoleId)
  scope: outputContainer
  properties: {
    principalId: scheduledJob.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: storageBlobDataContributorRoleId
  }
}

output jobName string = scheduledJob.name
output containerAppsEnvironmentName string = containerAppsEnvironment.name
output storageAccountName string = storageAccount.name
output blobContainerName string = outputContainer.name
