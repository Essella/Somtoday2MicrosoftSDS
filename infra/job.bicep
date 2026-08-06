targetScope = 'resourceGroup'

param jobName string
param location string
param tags object
param environmentId string
param imageReference string
param containerName string
param cronExpression string
param replicaTimeoutSeconds int
param replicaRetryLimit int
param environmentVariables array

@secure()
param somtodayClientSecret string

resource job 'Microsoft.App/jobs@2024-03-01' = {
  name: jobName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    environmentId: environmentId
    configuration: {
      replicaRetryLimit: replicaRetryLimit
      replicaTimeout: replicaTimeoutSeconds
      triggerType: 'Schedule'
      scheduleTriggerConfig: {
        cronExpression: cronExpression
        parallelism: 1
        replicaCompletionCount: 1
      }
      secrets: [
        {
          name: 'somtoday-client-secret'
          value: somtodayClientSecret
        }
      ]
    }
    template: {
      containers: [
        {
          name: containerName
          image: imageReference
          env: concat(environmentVariables, [
            {
              name: 'Somtoday__ClientSecret'
              secretRef: 'somtoday-client-secret'
            }
          ])
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
    }
  }
}

output principalId string = job.identity.principalId
output jobName string = job.name
