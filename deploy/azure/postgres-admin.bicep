// Makes a managed identity an Entra administrator of a PostgreSQL Flexible Server.
// A separate module because the administrator's resource name is the identity's object id,
// which is only known once the identity exists; module parameters may be runtime values.

param serverName string
param principalId string
param principalName string
param tenantId string

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' existing = {
  name: serverName
}

resource admin 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2024-08-01' = {
  parent: postgres
  name: principalId
  properties: {
    principalType: 'ServicePrincipal'
    principalName: principalName
    tenantId: tenantId
  }
}
