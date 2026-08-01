targetScope = 'resourceGroup'

@description('Korte, unieke prefix voor alle resources. Gebruik alleen kleine letters, cijfers en koppeltekens.')
@minLength(3)
@maxLength(12)
param namePrefix string = 'somtodaysds'

@description('Azure-regio voor alle resources.')
param location string = resourceGroup().location

@description('Publieke GHCR-image inclusief tag of digest.')
param imageReference string = 'ghcr.io/essella/somtoday2microsoftsds:latest'

@description('Cron-expressie in UTC.')
param cronExpression string = '0 1 * * *'

@description('CPU per jobreplica.')
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
param cpu string = '0.5'

@description('Geheugen per jobreplica.')
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

@description('Maximale looptijd per replica in seconden.')
@minValue(1)
@maxValue(3600)
param replicaTimeoutSeconds int = 3600

@description('Aantal retries na een mislukte replica.')
@minValue(0)
param replicaRetryLimit int = 1

@description('Somtoday-school-UUIDs.')
@minLength(1)
param schoolUuids array

@description('Somtoday OAuth-client-ID.')
@minLength(1)
param somtodayClientId string

@description('Somtoday-omgeving: PROD, TEST, ACCEPTATIE of NIGHTLY.')
@allowed([
  'PROD'
  'TEST'
  'ACCEPTATIE'
  'NIGHTLY'
])
param somtodayEnvironment string = 'PROD'

@description('Optionele tijdelijke bootstrap- of rotatiewaarde. Deploy na succesvolle opslag opnieuw met een lege waarde.')
@secure()
param somtodayClientSecret string = ''

@description('Naam van het Somtoday-secret in Key Vault.')
param somtodayClientSecretName string = 'somtoday-client-secret'

@description('Alleen deze Somtoday-locatiecodes opnemen; leeg betekent alle locaties.')
param includedLocationCodes array = []

@description('Deze Somtoday-locatiecodes uitsluiten.')
param excludedLocationCodes array = []

@description('Genereer School Data Sync guardian-relaties.')
param enableGuardianSync bool = false

@description('Gebruikersnaamformaat voor medewerkers.')
param teacherUsernameFormat string = 'Emailadres'

@description('Gebruikersnaamformaat voor leerlingen.')
param studentUsernameFormat string = 'Emailadres'

@description('Blobcontainer voor de CSV-uitvoer.')
param blobContainerName string = 'sds'

@description('Virtuele map in de Blobcontainer.')
param outputFolder string = 'sds/output'

@description('Altijd lege CSV-bestanden genereren.')
param generateEmptyCsv bool = false

@description('Maak een afzonderlijke uitvoermap per Somtoday-instelling.')
param separateByInstitution bool = true

@description('Maak een afzonderlijke uitvoermap per Somtoday-vestiging.')
param separateByLocation bool = false

@description('Tags voor alle resources die tags ondersteunen.')
param tags object = {
  application: 'Somtoday2MicrosoftSDS'
}

var normalizedPrefix = toLower(namePrefix)
var uniqueSuffix = uniqueString(subscription().subscriptionId, resourceGroup().id)
var storageAccountName = take('sds${replace(normalizedPrefix, '-', '')}${uniqueSuffix}', 24)
var keyVaultName = take('${normalizedPrefix}-${uniqueSuffix}', 24)
var logAnalyticsName = '${normalizedPrefix}-logs'
var environmentName = '${normalizedPrefix}-env'
var jobName = '${normalizedPrefix}-job'
var containerName = 'somtoday2microsoftsds'

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
    name: 'KeyVault__VaultUri'
    value: keyVault.properties.vaultUri
  }
  {
    name: 'KeyVault__SomtodayClientSecretName'
    value: somtodayClientSecretName
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
    value: outputFolder
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

var bootstrapEnvironmentVariables = empty(somtodayClientSecret) ? [] : [
  {
    name: 'Somtoday__ClientSecret'
    secretRef: 'somtoday-client-secret-bootstrap'
  }
]

var jobSecrets = empty(somtodayClientSecret) ? [] : [
  {
    name: 'somtoday-client-secret-bootstrap'
    value: somtodayClientSecret
  }
]

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
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

resource outputContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: blobContainerName
  properties: {
    publicAccess: 'None'
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    accessPolicies: []
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    retentionInDays: 30
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
  tags: tags
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
  location: location
  tags: tags
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
            bootstrapEnvironmentVariables
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

var keyVaultSecretsOfficerRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
)

resource keyVaultRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, scheduledJob.id, keyVaultSecretsOfficerRoleId)
  scope: keyVault
  properties: {
    principalId: scheduledJob.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: keyVaultSecretsOfficerRoleId
  }
}

output jobName string = scheduledJob.name
output containerAppsEnvironmentName string = containerAppsEnvironment.name
output storageAccountName string = storageAccount.name
output blobContainerName string = outputContainer.name
output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
