// PyroLens on Azure: PostgreSQL Flexible Server (PostGIS, Entra-only sign-in), a Container App for the API + map,
// and two scheduled Container Apps Jobs for ingestion and forecasts. No passwords: one user-assigned managed
// identity signs in to PostgreSQL and reads the FIRMS key from Key Vault.
//
// Deployed in two phases by deploy.ps1:
//   1. deployApps=false  -> identity, Key Vault, PostgreSQL, Container Apps environment
//   2. (you store the FIRMS key in Key Vault)
//   3. deployApps=true   -> the API and the two jobs, which reference that secret

targetScope = 'resourceGroup'

@description('Azure region for every resource.')
param location string = resourceGroup().location

@description('Object id of the person deploying; gets permission to write the FIRMS key into Key Vault.')
param adminPrincipalId string

@description('Create the API and jobs (phase 2). Needs the firms-map-key secret in Key Vault.')
param deployApps bool = false

param apiImage string = 'ghcr.io/nikolhs123/pyrolens-api:latest'
param workerImage string = 'ghcr.io/nikolhs123/pyrolens-worker:latest'

var suffix = uniqueString(resourceGroup().id)
var tenantId = tenant().tenantId
var databaseName = 'pyrolens'

// Built-in role definition ids.
var keyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'
var keyVaultSecretsOfficer = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-pyrolens'
  location: location
}

// ---------- Secrets ----------

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-pyro-${take(suffix, 13)}'
  location: location
  properties: {
    tenantId: tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

resource vaultReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, identity.id, keyVaultSecretsUser)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUser)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource vaultAdmin 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, adminPrincipalId, keyVaultSecretsOfficer)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsOfficer)
    principalId: adminPrincipalId
    principalType: 'User'
  }
}

// ---------- Database ----------

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: 'psql-pyrolens-${suffix}'
  location: location
  sku: { name: 'Standard_B1ms', tier: 'Burstable' }
  properties: {
    version: '16'
    storage: { storageSizeGB: 32, autoGrow: 'Disabled' }
    backup: { backupRetentionDays: 7, geoRedundantBackup: 'Disabled' }
    highAvailability: { mode: 'Disabled' }
    authConfig: { activeDirectoryAuth: 'Enabled', passwordAuth: 'Disabled', tenantId: tenantId }
    network: { publicNetworkAccess: 'Enabled' }
  }
}

// Child resources of a flexible server must not be changed in parallel, hence the dependsOn chain.
resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: databaseName
  properties: { charset: 'UTF8', collation: 'en_US.utf8' }
}

resource allowPostgis 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: postgres
  name: 'azure.extensions'
  properties: { value: 'POSTGIS', source: 'user-override' }
  dependsOn: [ database ]
}

// 0.0.0.0 = connections from Azure services only (the Container Apps), not the internet at large.
resource allowAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: postgres
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
  dependsOn: [ allowPostgis ]
}

// The app's identity is an Entra administrator, so it can create the PostGIS extension and the schema.
module postgresAdmin 'postgres-admin.bicep' = {
  name: 'postgres-admin'
  params: {
    serverName: postgres.name
    principalId: identity.properties.principalId
    principalName: identity.name
    tenantId: tenantId
  }
  dependsOn: [ allowAzure ]
}

// ---------- Container Apps ----------

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-pyrolens-${suffix}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-pyrolens'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

var databaseEnv = [
  {
    name: 'ConnectionStrings__PyroLens'
    value: 'Host=${postgres.properties.fullyQualifiedDomainName};Database=${databaseName};Username=${identity.name};SSL Mode=VerifyFull'
  }
  { name: 'Database__UseEntraId', value: 'true' }
  { name: 'Database__ManagedIdentityClientId', value: identity.properties.clientId }
]

var identityConfig = {
  type: 'UserAssigned'
  userAssignedIdentities: { '${identity.id}': {} }
}

resource api 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
  name: 'ca-pyrolens-api'
  location: location
  identity: identityConfig
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      ingress: { external: true, targetPort: 8080, transport: 'auto', allowInsecure: false }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: databaseEnv
          probes: [
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 }, periodSeconds: 30 }
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 }, periodSeconds: 30, failureThreshold: 3 }
          ]
        }
      ]
      // Scales to zero when nobody visits; the first visit then waits a few seconds for a cold start.
      scale: { minReplicas: 0, maxReplicas: 2 }
    }
  }
  dependsOn: [ postgresAdmin ]
}

var firmsKeySecret = {
  name: 'firms-map-key'
  keyVaultUrl: '${vault.properties.vaultUri}secrets/firms-map-key'
  identity: identity.id
}

resource ingestJob 'Microsoft.App/jobs@2024-03-01' = if (deployApps) {
  name: 'job-pyrolens-ingest'
  location: location
  identity: identityConfig
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Schedule'
      scheduleTriggerConfig: { cronExpression: '*/10 * * * *', parallelism: 1, replicaCompletionCount: 1 }
      replicaTimeout: 300
      replicaRetryLimit: 0
      secrets: [ firmsKeySecret ]
    }
    template: {
      containers: [
        {
          name: 'ingest'
          image: workerImage
          args: [ '--once', 'ingestion' ]
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: concat(databaseEnv, [
            { name: 'Firms__Mode', value: 'Api' }
            { name: 'Firms__MapKey', secretRef: 'firms-map-key' }
          ])
        }
      ]
    }
  }
  dependsOn: [ postgresAdmin, vaultReader ]
}

resource forecastJob 'Microsoft.App/jobs@2024-03-01' = if (deployApps) {
  name: 'job-pyrolens-forecast'
  location: location
  identity: identityConfig
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Schedule'
      // A few minutes after ingestion, twice an hour.
      scheduleTriggerConfig: { cronExpression: '5,35 * * * *', parallelism: 1, replicaCompletionCount: 1 }
      replicaTimeout: 300
      replicaRetryLimit: 0
    }
    template: {
      containers: [
        {
          name: 'forecast'
          image: workerImage
          args: [ '--once', 'forecast' ]
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: databaseEnv
        }
      ]
    }
  }
  dependsOn: [ postgresAdmin ]
}

output keyVaultName string = vault.name
output postgresHost string = postgres.properties.fullyQualifiedDomainName
output apiUrl string = deployApps ? 'https://${api!.properties.configuration.ingress.fqdn}' : ''
